import { randomBytes, scrypt as scryptCb, timingSafeEqual, createHash, randomUUID } from "node:crypto";
import { promisify } from "node:util";
import { Router } from "express";
import { db } from "./db.js";

// Authentification du parent (dashboard). Un seul compte parent, cree au premier acces.
// - Mot de passe : scrypt avec sel aleatoire (jamais stocke en clair).
// - Session : jeton aleatoire de 256 bits dans un cookie httpOnly + SameSite=Strict ; seul son hash
//   SHA-256 est stocke en base (une fuite de la base ne donne pas de sessions utilisables).
// - Les appareils (clients Windows) ne passent PAS par ici : ils s'authentifient par cle RSA sur le
//   WebSocket /ws/device, et par code a usage unique pour le pairing.
const scrypt = promisify(scryptCb);

const COOKIE_NAME = "nautilyou_session";
const SESSION_DAYS = 14;
const MIN_PASSWORD_LENGTH = 8;

async function hashPassword(password) {
  const salt = randomBytes(16);
  const hash = await scrypt(password, salt, 64);
  return `scrypt$${salt.toString("hex")}$${hash.toString("hex")}`;
}

async function verifyPassword(password, stored) {
  const [scheme, saltHex, hashHex] = String(stored).split("$");
  if (scheme !== "scrypt" || !saltHex || !hashHex) return false;
  const expected = Buffer.from(hashHex, "hex");
  const actual = await scrypt(password, Buffer.from(saltHex, "hex"), expected.length);
  return timingSafeEqual(actual, expected);
}

// Hash factice verifie quand l'identifiant n'existe pas, pour que le temps de reponse ne revele pas
// si un identifiant existe.
const DUMMY_HASH = await hashPassword(randomBytes(8).toString("hex"));

function parentCount() {
  return db.prepare("SELECT COUNT(*) AS n FROM parents").get().n;
}

function tokenHash(token) {
  return createHash("sha256").update(token).digest("hex");
}

function readCookie(req, name) {
  const header = req.headers.cookie;
  if (!header) return null;
  for (const part of header.split(";")) {
    const [k, ...rest] = part.trim().split("=");
    if (k === name) return decodeURIComponent(rest.join("="));
  }
  return null;
}

function sessionCookie(req, token, maxAgeSeconds) {
  const parts = [
    `${COOKIE_NAME}=${encodeURIComponent(token)}`,
    "Path=/",
    "HttpOnly",
    "SameSite=Strict",
    `Max-Age=${maxAgeSeconds}`,
  ];
  if (req.secure) parts.push("Secure");
  return parts.join("; ");
}

function createSession(req, res, parentId) {
  const token = randomBytes(32).toString("base64url");
  const expires = new Date(Date.now() + SESSION_DAYS * 24 * 3600 * 1000).toISOString();
  db.prepare("INSERT INTO sessions (token_hash, parent_id, expires_at) VALUES (?, ?, ?)").run(
    tokenHash(token),
    parentId,
    expires
  );
  res.setHeader("Set-Cookie", sessionCookie(req, token, SESSION_DAYS * 24 * 3600));
}

function currentParent(req) {
  const token = readCookie(req, COOKIE_NAME);
  if (!token) return null;
  const row = db
    .prepare(
      `SELECT p.id, p.username, s.expires_at FROM sessions s JOIN parents p ON p.id = s.parent_id
       WHERE s.token_hash = ?`
    )
    .get(tokenHash(token));
  if (!row) return null;
  if (new Date(row.expires_at) < new Date()) {
    db.prepare("DELETE FROM sessions WHERE token_hash = ?").run(tokenHash(token));
    return null;
  }
  return { id: row.id, username: row.username };
}

export function requireAuth(req, res, next) {
  const parent = currentParent(req);
  if (!parent) return res.status(401).json({ error: "non authentifié" });
  req.parent = parent;
  next();
}

// Protection CSRF en plus de SameSite=Strict : une requete qui modifie des donnees doit venir de la
// meme origine que le serveur (l'en-tete Origin, quand present, doit correspondre a l'hote).
export function sameOriginOnly(req, res, next) {
  if (req.method === "GET" || req.method === "HEAD" || req.method === "OPTIONS") return next();
  const origin = req.headers.origin;
  if (origin) {
    let host;
    try {
      host = new URL(origin).host;
    } catch {
      return res.status(403).json({ error: "origine invalide" });
    }
    if (host !== req.headers.host) return res.status(403).json({ error: "origine refusée" });
  }
  next();
}

// Limitation des tentatives de connexion par adresse : apres 5 echecs, blocage temporaire croissant.
const failures = new Map(); // ip -> { count, blockedUntil }

function checkBlocked(ip) {
  const f = failures.get(ip);
  if (f && f.blockedUntil > Date.now()) return Math.ceil((f.blockedUntil - Date.now()) / 1000);
  return 0;
}

function registerFailure(ip) {
  const f = failures.get(ip) || { count: 0, blockedUntil: 0 };
  f.count += 1;
  if (f.count >= 5) {
    const seconds = Math.min(15 * 60, 30 * 2 ** (f.count - 5));
    f.blockedUntil = Date.now() + seconds * 1000;
  }
  failures.set(ip, f);
}

function validateCredentials(username, password) {
  if (typeof username !== "string" || username.trim().length < 3) {
    return "L'identifiant doit contenir au moins 3 caractères.";
  }
  if (typeof password !== "string" || password.length < MIN_PASSWORD_LENGTH) {
    return `Le mot de passe doit contenir au moins ${MIN_PASSWORD_LENGTH} caractères.`;
  }
  return null;
}

export const authRouter = Router();

// Etat pour le dashboard : faut-il creer le compte, ou se connecter, ou est-on deja connecte.
authRouter.get("/status", (req, res) => {
  const parent = currentParent(req);
  res.json({
    setupRequired: parentCount() === 0,
    authenticated: Boolean(parent),
    username: parent ? parent.username : null,
  });
});

// Creation du compte parent : uniquement tant qu'aucun compte n'existe.
authRouter.post("/setup", async (req, res) => {
  const { username, password } = req.body || {};
  const problem = validateCredentials(username, password);
  if (problem) return res.status(400).json({ error: problem });

  const id = randomUUID();
  const hash = await hashPassword(password);
  // INSERT conditionnel : deux requetes simultanees ne peuvent pas creer deux comptes.
  const result = db
    .prepare("INSERT INTO parents (id, username, password_hash) SELECT ?, ?, ? WHERE NOT EXISTS (SELECT 1 FROM parents)")
    .run(id, username.trim(), hash);
  if (result.changes === 0) return res.status(403).json({ error: "Le compte parent existe déjà." });

  createSession(req, res, id);
  res.status(201).json({ username: username.trim() });
});

authRouter.post("/login", async (req, res) => {
  const ip = req.ip;
  const wait = checkBlocked(ip);
  if (wait > 0) {
    return res.status(429).json({ error: `Trop de tentatives. Réessaie dans ${wait} s.` });
  }

  const { username, password } = req.body || {};
  const row =
    typeof username === "string" ? db.prepare("SELECT * FROM parents WHERE username = ?").get(username.trim()) : null;
  const ok = await verifyPassword(typeof password === "string" ? password : "", row ? row.password_hash : DUMMY_HASH);

  if (!row || !ok) {
    registerFailure(ip);
    return res.status(401).json({ error: "Identifiant ou mot de passe incorrect." });
  }

  failures.delete(ip);
  createSession(req, res, row.id);
  res.json({ username: row.username });
});

authRouter.post("/logout", (req, res) => {
  const token = readCookie(req, COOKIE_NAME);
  if (token) db.prepare("DELETE FROM sessions WHERE token_hash = ?").run(tokenHash(token));
  res.setHeader("Set-Cookie", sessionCookie(req, "", 0));
  res.status(204).end();
});
