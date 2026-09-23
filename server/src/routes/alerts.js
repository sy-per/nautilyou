import { Router } from "express";
import { db } from "../db.js";

export const alertsRouter = Router();

alertsRouter.get("/", (req, res) => {
  const rows = db.prepare("SELECT * FROM alerts ORDER BY created_at DESC LIMIT 50").all();
  res.json({
    alerts: rows.map((r) => ({
      id: r.id,
      deviceId: r.device_id,
      text: r.text,
      severity: r.severity,
      read: Boolean(r.read),
      createdAt: r.created_at,
    })),
  });
});

alertsRouter.post("/:id/read", (req, res) => {
  db.prepare("UPDATE alerts SET read = 1 WHERE id = ?").run(req.params.id);
  res.status(204).end();
});
