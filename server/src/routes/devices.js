import { Router } from "express";
import { randomUUID, createPublicKey } from "node:crypto";
import { db } from "../db.js";
import { deviceDetail } from "../deviceDetail.js";
import { today } from "../util.js";
import { pushConfigToDevice, pushCommand } from "../deviceSocket.js";

export const devicesRouter = Router();
export const pairingRouter = Router();

devicesRouter.get("/:id", (req, res) => {
  const row = db.prepare("SELECT * FROM devices WHERE id = ?").get(req.params.id);
  if (!row) return res.status(404).json({ error: "appareil introuvable" });
  res.json({ device: deviceDetail(row) });
});

devicesRouter.put("/:id/overrides", (req, res) => {
  const row = db.prepare("SELECT * FROM devices WHERE id = ?").get(req.params.id);
  if (!row) return res.status(404).json({ error: "appareil introuvable" });
  const { overrides } = req.body || {};
  if (!overrides || typeof overrides !== "object") {
    return res.status(400).json({ error: "overrides requis" });
  }
  db.prepare("UPDATE devices SET overrides = ? WHERE id = ?").run(JSON.stringify(overrides), req.params.id);
  pushConfigToDevice(req.params.id);
  const updated = db.prepare("SELECT * FROM devices WHERE id = ?").get(req.params.id);
  res.json({ device: deviceDetail(updated) });
});

devicesRouter.delete("/:id", (req, res) => {
  db.prepare("DELETE FROM devices WHERE id = ?").run(req.params.id);
  res.status(204).end();
});

// Action instantanée : verrouillage immédiat, poussé tout de suite si l'appareil est connecté
// au WebSocket ; sinon enfilée pour être délivrée à sa prochaine connexion.
devicesRouter.post("/:id/lock", (req, res) => {
  const row = db.prepare("SELECT id FROM devices WHERE id = ?").get(req.params.id);
  if (!row) return res.status(404).json({ error: "appareil introuvable" });
  db.prepare("INSERT INTO instant_commands (device_id, type, payload) VALUES (?, 'lock', '{}')").run(req.params.id);
  const delivered = pushCommand(req.params.id, { type: "lock" });
  res.status(202).json({ status: delivered ? "commande envoyee en direct" : "commande mise en file (appareil hors ligne)" });
});

// Action instantanée : bonus temps. Ajoute des minutes au quota du jour pour cet appareil.
devicesRouter.post("/:id/bonus", (req, res) => {
  const row = db.prepare("SELECT id FROM devices WHERE id = ?").get(req.params.id);
  if (!row) return res.status(404).json({ error: "appareil introuvable" });
  const { minutes } = req.body || {};
  if (!Number.isInteger(minutes) || minutes <= 0) {
    return res.status(400).json({ error: "minutes doit être un entier positif" });
  }
  db.prepare("INSERT INTO bonus_grants (device_id, date, minutes) VALUES (?, ?, ?)").run(
    req.params.id,
    today(),
    minutes
  );
  db.prepare(
    "INSERT INTO instant_commands (device_id, type, payload) VALUES (?, 'bonus', ?)"
  ).run(req.params.id, JSON.stringify({ minutes }));
  pushConfigToDevice(req.params.id);

  const updated = db.prepare("SELECT * FROM devices WHERE id = ?").get(req.params.id);
  res.status(201).json({ device: deviceDetail(updated) });
});

// Applications installees remontees par l'appareil (noms affiches, tries), pour le selecteur d'apps.
devicesRouter.get("/:id/apps", (req, res) => {
  const row = db.prepare("SELECT apps, updated_at FROM device_apps WHERE device_id = ?").get(req.params.id);
  const apps = row ? JSON.parse(row.apps) : [];
  res.json({ apps: apps.map((a) => a.name).sort((a, b) => a.localeCompare(b)), updatedAt: row ? row.updated_at : null });
});

devicesRouter.get("/:id/activity", (req, res) => {
  const days = Number(req.query.days) || 7;
  const rows = db
    .prepare("SELECT * FROM activity_snapshots WHERE device_id = ? ORDER BY date DESC LIMIT ?")
    .all(req.params.id, days);
  res.json({
    activity: rows.map((r) => ({
      date: r.date,
      usedMinutes: r.used_minutes,
      topSites: JSON.parse(r.top_sites),
      topApps: JSON.parse(r.top_apps),
      blockedAppsCount: r.blocked_apps_count,
    })),
  });
});

// Le client complète l'appairage avec le code affiché dans le dashboard + sa clé publique.
// Cette même clé sert ensuite de jeton d'authentification pour la connexion WebSocket (voir
// deviceSocket.js) — TODO sécurité : remplacer par une vraie paire de clés asymétriques.
pairingRouter.post("/complete", (req, res) => {
  const { code, deviceName, model, publicKey } = req.body || {};
  if (!code || !publicKey) {
    return res.status(400).json({ error: "code et publicKey requis" });
  }

  try {
    createPublicKey({ key: Buffer.from(publicKey, "base64"), format: "der", type: "spki" });
  } catch {
    return res.status(400).json({ error: "publicKey invalide (attendu : clé RSA au format SPKI, en base64)" });
  }

  const pairing = db.prepare("SELECT * FROM pairing_codes WHERE code = ?").get(code);
  if (!pairing) return res.status(404).json({ error: "code d'appairage invalide" });
  if (pairing.used_at) return res.status(410).json({ error: "code déjà utilisé" });
  if (new Date(pairing.expires_at) < new Date()) return res.status(410).json({ error: "code expiré" });

  const deviceId = randomUUID();
  db.prepare(
    "INSERT INTO devices (id, child_id, name, model, public_key, paired_at, online) VALUES (?, ?, ?, ?, ?, datetime('now'), 1)"
  ).run(deviceId, pairing.child_id, deviceName || pairing.device_name || "Nouvel appareil", model || null, publicKey);

  db.prepare("UPDATE pairing_codes SET used_at = datetime('now') WHERE code = ?").run(code);

  const device = db.prepare("SELECT * FROM devices WHERE id = ?").get(deviceId);
  res.status(201).json({ device: deviceDetail(device) });
});
