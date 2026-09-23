import express from "express";
import cors from "cors";
import { createServer as createHttpServer } from "node:http";
import { createServer as createHttpsServer } from "node:https";
import { readFileSync } from "node:fs";
import "./db.js";
import { childrenRouter } from "./routes/children.js";
import { devicesRouter, pairingRouter } from "./routes/devices.js";
import { alertsRouter } from "./routes/alerts.js";
import { requestsRouter } from "./routes/requests.js";
import { attachDeviceSocket } from "./deviceSocket.js";

const app = express();
app.use(cors());
app.use(express.json());

app.get("/api/health", (req, res) => res.json({ status: "ok" }));

app.use("/api/children", childrenRouter);
app.use("/api/devices", devicesRouter);
app.use("/api/pairing", pairingRouter);
app.use("/api/alerts", alertsRouter);
app.use("/api/requests", requestsRouter);

const port = Number(process.env.PORT) || 4100;

// TLS optionnel : si TLS_CERT_FILE et TLS_KEY_FILE (PEM) sont definis, le serveur ecoute en HTTPS/WSS
// directement. Sinon HTTP/WS en clair (reseau local de confiance, ou derriere un reverse proxy TLS).
const certFile = process.env.TLS_CERT_FILE;
const keyFile = process.env.TLS_KEY_FILE;
const useTls = Boolean(certFile && keyFile);
const httpServer = useTls
  ? createHttpsServer({ cert: readFileSync(certFile), key: readFileSync(keyFile) }, app)
  : createHttpServer(app);
attachDeviceSocket(httpServer);

httpServer.listen(port, () => {
  console.log(`Nautilyou server (API + WebSocket appareils) sur ${useTls ? "https" : "http"}://localhost:${port}`);
});
