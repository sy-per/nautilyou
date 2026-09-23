import { randomUUID } from "node:crypto";
import { db } from "./db.js";

// Partagé entre la route REST (routes/requests.js) et le canal WebSocket (deviceSocket.js) pour
// éviter un import circulaire entre les deux.
export function createRequest(deviceId, type, payload) {
  const id = randomUUID();
  db.prepare("INSERT INTO requests (id, device_id, type, payload) VALUES (?, ?, ?, ?)").run(
    id,
    deviceId,
    type,
    JSON.stringify(payload || {})
  );
  return id;
}
