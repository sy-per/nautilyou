import { Router } from "express";
import { randomUUID } from "node:crypto";
import { db } from "../db.js";
import { defaultConfigTemplate, resolveConfig } from "../config.js";
import { computeDeviceStatus } from "../status.js";
import { today } from "../util.js";
import { pushConfigToDevice } from "../deviceSocket.js";

export const childrenRouter = Router();

function rowToChild(row) {
  return {
    id: row.id,
    name: row.name,
    avatarColor: row.avatar_color,
    defaultConfig: JSON.parse(row.default_config),
    createdAt: row.created_at,
  };
}

function devicesForChild(childId, defaultConfig) {
  const rows = db
    .prepare("SELECT id, name, model, online, last_seen_at, overrides FROM devices WHERE child_id = ? ORDER BY created_at")
    .all(childId);
  const d0 = today();
  return rows.map((d) => {
    const overrides = JSON.parse(d.overrides);
    const config = resolveConfig(defaultConfig, overrides);
    const snapshot = db
      .prepare("SELECT used_minutes FROM activity_snapshots WHERE device_id = ? AND date = ?")
      .get(d.id, d0);
    const bonusRow = db
      .prepare("SELECT COALESCE(SUM(minutes), 0) as total FROM bonus_grants WHERE device_id = ? AND date = ?")
      .get(d.id, d0);
    const status = computeDeviceStatus(config, snapshot ? snapshot.used_minutes : 0, bonusRow.total);
    return {
      id: d.id,
      name: d.name,
      model: d.model,
      online: Boolean(d.online),
      lastSeenAt: d.last_seen_at,
      overrides,
      status,
    };
  });
}

// Statut agrégé de l'enfant pour le badge avatar : vert si au moins un appareil satisfait
// les 2 conditions (temps restant + dans la plage horaire), rouge sinon, "none" si aucun appareil.
function childStatus(devices) {
  if (devices.length === 0) return "none";
  return devices.some((d) => d.status.ok) ? "green" : "red";
}

function childWithDevices(row) {
  const child = rowToChild(row);
  const devices = devicesForChild(row.id, child.defaultConfig);
  return { ...child, devices, status: childStatus(devices) };
}

childrenRouter.get("/", (req, res) => {
  const rows = db.prepare("SELECT * FROM children ORDER BY created_at").all();
  res.json({ children: rows.map(childWithDevices) });
});

childrenRouter.post("/", (req, res) => {
  const { name, avatarColor } = req.body || {};
  if (!name || typeof name !== "string") {
    return res.status(400).json({ error: "name requis" });
  }
  const id = randomUUID();
  db.prepare(
    "INSERT INTO children (id, name, avatar_color, default_config) VALUES (?, ?, ?, ?)"
  ).run(id, name, avatarColor || "#7c9cff", JSON.stringify(defaultConfigTemplate()));
  const row = db.prepare("SELECT * FROM children WHERE id = ?").get(id);
  res.status(201).json({ child: childWithDevices(row) });
});

childrenRouter.get("/:id", (req, res) => {
  const row = db.prepare("SELECT * FROM children WHERE id = ?").get(req.params.id);
  if (!row) return res.status(404).json({ error: "enfant introuvable" });
  res.json({ child: childWithDevices(row) });
});

childrenRouter.put("/:id", (req, res) => {
  const row = db.prepare("SELECT * FROM children WHERE id = ?").get(req.params.id);
  if (!row) return res.status(404).json({ error: "enfant introuvable" });
  const { name, avatarColor } = req.body || {};
  db.prepare("UPDATE children SET name = COALESCE(?, name), avatar_color = COALESCE(?, avatar_color) WHERE id = ?").run(
    name || null,
    avatarColor || null,
    req.params.id
  );
  const updated = db.prepare("SELECT * FROM children WHERE id = ?").get(req.params.id);
  res.json({ child: childWithDevices(updated) });
});

childrenRouter.put("/:id/config", (req, res) => {
  const row = db.prepare("SELECT * FROM children WHERE id = ?").get(req.params.id);
  if (!row) return res.status(404).json({ error: "enfant introuvable" });
  const { defaultConfig } = req.body || {};
  if (!defaultConfig) return res.status(400).json({ error: "defaultConfig requis" });
  db.prepare("UPDATE children SET default_config = ? WHERE id = ?").run(
    JSON.stringify(defaultConfig),
    req.params.id
  );

  // La config par défaut a changé : pousse la config résolue à jour à tous les appareils
  // de l'enfant actuellement connectés (ceux avec un override complet sur la section modifiée
  // ne verront pas leur config effective changer, mais on repousse quand même par simplicité).
  const deviceIds = db.prepare("SELECT id FROM devices WHERE child_id = ?").all(req.params.id);
  for (const { id } of deviceIds) pushConfigToDevice(id);

  const updated = db.prepare("SELECT * FROM children WHERE id = ?").get(req.params.id);
  res.json({ child: childWithDevices(updated) });
});

// Union des applications installees sur tous les appareils de l'enfant (pour les reglages "tous les appareils").
childrenRouter.get("/:id/apps", (req, res) => {
  const rows = db
    .prepare("SELECT da.apps FROM device_apps da JOIN devices d ON d.id = da.device_id WHERE d.child_id = ?")
    .all(req.params.id);
  const names = new Set();
  for (const r of rows) for (const a of JSON.parse(r.apps)) names.add(a.name);
  res.json({ apps: [...names].sort((a, b) => a.localeCompare(b)) });
});

childrenRouter.delete("/:id", (req, res) => {
  db.prepare("DELETE FROM children WHERE id = ?").run(req.params.id);
  res.status(204).end();
});

// Génère un code d'appairage à usage unique pour ajouter un appareil à cet enfant.
childrenRouter.post("/:id/pairing-code", (req, res) => {
  const child = db.prepare("SELECT id FROM children WHERE id = ?").get(req.params.id);
  if (!child) return res.status(404).json({ error: "enfant introuvable" });

  const code = String(Math.floor(100000 + Math.random() * 900000)); // code à 6 chiffres
  const expiresAt = new Date(Date.now() + 15 * 60 * 1000).toISOString(); // 15 min
  const { deviceName } = req.body || {};

  db.prepare(
    "INSERT INTO pairing_codes (code, child_id, device_name, expires_at) VALUES (?, ?, ?, ?)"
  ).run(code, req.params.id, deviceName || null, expiresAt);

  res.status(201).json({ code, expiresAt });
});
