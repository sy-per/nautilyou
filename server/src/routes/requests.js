import { Router } from "express";
import { db } from "../db.js";
import { deviceDetail } from "../deviceDetail.js";
import { today } from "../util.js";
import { pushConfigToDevice, pushCommand } from "../deviceSocket.js";
import { createRequest } from "../requestsModel.js";

export const requestsRouter = Router();

function requestRow(row) {
  const device = db.prepare("SELECT id, name, child_id FROM devices WHERE id = ?").get(row.device_id);
  const child = device ? db.prepare("SELECT id, name FROM children WHERE id = ?").get(device.child_id) : null;
  return {
    id: row.id,
    deviceId: row.device_id,
    deviceName: device?.name || "Appareil supprimé",
    childId: child?.id || null,
    childName: child?.name || "?",
    type: row.type,
    payload: JSON.parse(row.payload),
    status: row.status,
    createdAt: row.created_at,
    resolvedAt: row.resolved_at,
  };
}

requestsRouter.post("/", (req, res) => {
  const { deviceId, type, payload } = req.body || {};
  if (!deviceId || !["time", "site"].includes(type)) {
    return res.status(400).json({ error: "deviceId et type ('time' ou 'site') requis" });
  }
  const device = db.prepare("SELECT id FROM devices WHERE id = ?").get(deviceId);
  if (!device) return res.status(404).json({ error: "appareil introuvable" });

  const id = createRequest(deviceId, type, payload);
  const row = db.prepare("SELECT * FROM requests WHERE id = ?").get(id);
  res.status(201).json({ request: requestRow(row) });
});

requestsRouter.get("/", (req, res) => {
  const status = req.query.status;
  const rows = status
    ? db.prepare("SELECT * FROM requests WHERE status = ? ORDER BY created_at DESC").all(status)
    : db.prepare("SELECT * FROM requests ORDER BY created_at DESC LIMIT 100").all();
  res.json({ requests: rows.map(requestRow) });
});

requestsRouter.post("/:id/approve", (req, res) => {
  const row = db.prepare("SELECT * FROM requests WHERE id = ?").get(req.params.id);
  if (!row) return res.status(404).json({ error: "demande introuvable" });
  if (row.status !== "pending") return res.status(410).json({ error: "demande déjà traitée" });

  const payload = JSON.parse(row.payload);
  const deviceRow = db.prepare("SELECT * FROM devices WHERE id = ?").get(row.device_id);
  if (!deviceRow) return res.status(404).json({ error: "appareil introuvable" });

  if (row.type === "time") {
    const minutes = Number(payload.minutes) || 15;
    db.prepare("INSERT INTO bonus_grants (device_id, date, minutes) VALUES (?, ?, ?)").run(
      row.device_id,
      today(),
      minutes
    );
  } else if (row.type === "site") {
    const domain = String(payload.domain || "").trim();
    if (domain) {
      const current = deviceDetail(deviceRow).config.web;
      const overrides = JSON.parse(deviceRow.overrides);
      const newWeb = { ...current };
      if (current.whitelistMode) {
        if (!newWeb.whitelist.includes(domain)) newWeb.whitelist = [...newWeb.whitelist, domain];
      } else {
        newWeb.blacklist = newWeb.blacklist.filter((d) => d !== domain);
        // Site bloque par une liste publique du filtre enfant : on l'ajoute aux exceptions.
        if (newWeb.childFilter?.enabled && !newWeb.whitelist.includes(domain)) {
          newWeb.whitelist = [...newWeb.whitelist, domain];
        }
      }
      db.prepare("UPDATE devices SET overrides = ? WHERE id = ?").run(
        JSON.stringify({ ...overrides, web: newWeb }),
        row.device_id
      );
    }
  }

  db.prepare("UPDATE requests SET status = 'approved', resolved_at = datetime('now') WHERE id = ?").run(req.params.id);
  pushConfigToDevice(row.device_id);
  pushCommand(row.device_id, { type: "request-resolved", requestId: row.id, status: "approved", payload });

  const updated = db.prepare("SELECT * FROM requests WHERE id = ?").get(req.params.id);
  res.json({ request: requestRow(updated) });
});

requestsRouter.post("/:id/deny", (req, res) => {
  const row = db.prepare("SELECT * FROM requests WHERE id = ?").get(req.params.id);
  if (!row) return res.status(404).json({ error: "demande introuvable" });
  if (row.status !== "pending") return res.status(410).json({ error: "demande déjà traitée" });

  db.prepare("UPDATE requests SET status = 'denied', resolved_at = datetime('now') WHERE id = ?").run(req.params.id);
  pushCommand(row.device_id, { type: "request-resolved", requestId: row.id, status: "denied", payload: JSON.parse(row.payload) });

  const updated = db.prepare("SELECT * FROM requests WHERE id = ?").get(req.params.id);
  res.json({ request: requestRow(updated) });
});
