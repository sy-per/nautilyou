import { WebSocketServer } from "ws";
import { randomBytes, createPublicKey, verify as verifySignature } from "node:crypto";
import { db } from "./db.js";
import { deviceDetail, deviceDetailById } from "./deviceDetail.js";
import { today } from "./util.js";
import { createRequest } from "./requestsModel.js";

// Canal de données temps réel entre le serveur et un client appareil : WebSocket persistant et
// authentifié, remplace le polling HTTP (et remplace aussi le relai WebRTC/signaling envisagé au
// départ — voir dev-context, section "Décision P2P → WebSocket").
//
// Authentification par défi/réponse RSA (2026-09-23) : à la connexion, le serveur envoie un nonce
// aléatoire ; le client le signe avec sa clé privée (jamais transmise) et renvoie la signature ;
// le serveur la vérifie avec la clé publique enregistrée au pairing. Aucun secret statique ne
// transite sur le fil (contrairement à la V1 qui envoyait la "clé" en clair comme un mot de passe).
const connections = new Map(); // deviceId -> ws
const AUTH_TIMEOUT_MS = 5000;

export function attachDeviceSocket(httpServer) {
  const wss = new WebSocketServer({ server: httpServer, path: "/ws/device" });

  wss.on("connection", (ws, req) => {
    const url = new URL(req.url, "http://localhost");
    const deviceId = url.searchParams.get("deviceId");

    const row = db.prepare("SELECT * FROM devices WHERE id = ?").get(deviceId);
    if (!row) {
      ws.close(4004, "appareil introuvable");
      return;
    }

    let publicKey;
    try {
      publicKey = createPublicKey({ key: Buffer.from(row.public_key, "base64"), format: "der", type: "spki" });
    } catch {
      ws.close(4001, "cle publique invalide");
      return;
    }

    const nonce = randomBytes(32);
    const authTimer = setTimeout(() => ws.close(4001, "authentification expiree"), AUTH_TIMEOUT_MS);

    ws.once("message", (raw) => {
      clearTimeout(authTimer);
      let msg;
      try {
        msg = JSON.parse(raw.toString());
      } catch {
        ws.close(4001, "message invalide");
        return;
      }
      if (msg.type !== "auth" || !msg.signature) {
        ws.close(4001, "authentification requise");
        return;
      }

      let signatureOk = false;
      try {
        signatureOk = verifySignature("sha256", nonce, publicKey, Buffer.from(msg.signature, "base64"));
      } catch {
        signatureOk = false;
      }

      if (!signatureOk) {
        ws.close(4001, "signature invalide");
        return;
      }

      onAuthenticated(ws, deviceId, row);
    });

    sendJson(ws, { type: "challenge", nonce: nonce.toString("base64") });
  });

  return wss;
}

function onAuthenticated(ws, deviceId, row) {
  connections.set(deviceId, ws);
  db.prepare("UPDATE devices SET online = 1, last_seen_at = datetime('now') WHERE id = ?").run(deviceId);

  sendJson(ws, { type: "config", payload: deviceDetail(row) });

  ws.on("message", (raw) => {
    let msg;
    try {
      msg = JSON.parse(raw.toString());
    } catch {
      return;
    }
    handleClientMessage(deviceId, msg);
  });

  ws.on("close", () => {
    if (connections.get(deviceId) === ws) {
      connections.delete(deviceId);
      db.prepare("UPDATE devices SET online = 0 WHERE id = ?").run(deviceId);
    }
  });
}

function handleClientMessage(deviceId, msg) {
  if (msg.type === "activity") {
    const { usedMinutes, topSites, topApps, blockedAppsCount, date } = msg.payload || {};
    const d = date || today();
    db.prepare(
      `INSERT INTO activity_snapshots (device_id, date, used_minutes, top_sites, top_apps, blocked_apps_count)
       VALUES (?, ?, ?, ?, ?, ?)
       ON CONFLICT(device_id, date) DO UPDATE SET
         used_minutes = excluded.used_minutes,
         top_sites = excluded.top_sites,
         top_apps = excluded.top_apps,
         blocked_apps_count = excluded.blocked_apps_count`
    ).run(
      deviceId,
      d,
      usedMinutes || 0,
      JSON.stringify(topSites || []),
      JSON.stringify(topApps || []),
      blockedAppsCount || 0
    );
    db.prepare("UPDATE devices SET last_seen_at = datetime('now') WHERE id = ?").run(deviceId);
  } else if (msg.type === "apps") {
    const apps = Array.isArray(msg.payload?.apps) ? msg.payload.apps : [];
    const clean = apps
      .filter((a) => a && typeof a.name === "string" && a.name.trim())
      .slice(0, 500)
      .map((a) => ({ name: a.name.trim(), exes: Array.isArray(a.exes) ? a.exes.filter((e) => typeof e === "string") : [] }));
    db.prepare(
      `INSERT INTO device_apps (device_id, apps, updated_at) VALUES (?, ?, datetime('now'))
       ON CONFLICT(device_id) DO UPDATE SET apps = excluded.apps, updated_at = excluded.updated_at`
    ).run(deviceId, JSON.stringify(clean));
  } else if (msg.type === "request") {
    const { type, payload } = msg.payload || {};
    if (type === "time" || type === "site") {
      createRequest(deviceId, type, payload);
    }
  }
}

function sendJson(ws, obj) {
  if (ws.readyState === ws.OPEN) ws.send(JSON.stringify(obj));
}

// À appeler par les routes REST du dashboard après toute modification touchant un appareil
// (override, bonus, config par défaut de l'enfant) pour pousser instantanément sa config à jour.
export function pushConfigToDevice(deviceId) {
  const ws = connections.get(deviceId);
  if (!ws) return false;
  const detail = deviceDetailById(deviceId);
  if (!detail) return false;
  sendJson(ws, { type: "config", payload: detail });
  return true;
}

// Pousse une commande instantanée (verrouillage, bonus temps...) si l'appareil est connecté.
export function pushCommand(deviceId, command) {
  const ws = connections.get(deviceId);
  if (!ws) return false;
  sendJson(ws, { type: "command", payload: command });
  return true;
}

export function isDeviceConnected(deviceId) {
  return connections.has(deviceId);
}
