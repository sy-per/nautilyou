import { db } from "./db.js";
import { resolveConfig, hasOverride } from "./config.js";
import { computeDeviceStatus } from "./status.js";
import { today } from "./util.js";

// Construit la vue complète d'un appareil (config résolue, statut, activité) — utilisé par les
// routes REST et par le canal WebSocket (push de config en temps réel).
export function deviceDetail(deviceRow) {
  const child = db.prepare("SELECT * FROM children WHERE id = ?").get(deviceRow.child_id);
  const overrides = JSON.parse(deviceRow.overrides);
  const defaultConfig = JSON.parse(child.default_config);
  const config = resolveConfig(defaultConfig, overrides);

  const snapshot = db
    .prepare("SELECT * FROM activity_snapshots WHERE device_id = ? AND date = ?")
    .get(deviceRow.id, today());

  const bonusRow = db
    .prepare("SELECT COALESCE(SUM(minutes), 0) as total FROM bonus_grants WHERE device_id = ? AND date = ?")
    .get(deviceRow.id, today());

  return {
    id: deviceRow.id,
    childId: deviceRow.child_id,
    name: deviceRow.name,
    model: deviceRow.model,
    online: Boolean(deviceRow.online),
    lastSeenAt: deviceRow.last_seen_at,
    overrides,
    hasOverride: {
      time: hasOverride(overrides, "time"),
      web: hasOverride(overrides, "web"),
      apps: hasOverride(overrides, "apps"),
    },
    config,
    bonusMinutesToday: bonusRow.total,
    status: computeDeviceStatus(config, snapshot ? snapshot.used_minutes : 0, bonusRow.total),
    activity: {
      usedMinutesToday: snapshot ? snapshot.used_minutes : 0,
      topSites: snapshot ? JSON.parse(snapshot.top_sites) : [],
      topApps: snapshot ? JSON.parse(snapshot.top_apps) : [],
      blockedAppsCount: snapshot ? snapshot.blocked_apps_count : config.apps.blockedApps.length,
    },
  };
}

export function deviceDetailById(deviceId) {
  const row = db.prepare("SELECT * FROM devices WHERE id = ?").get(deviceId);
  return row ? deviceDetail(row) : null;
}
