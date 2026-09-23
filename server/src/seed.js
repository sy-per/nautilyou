// Peuple la base avec des données proches du mock UI (Eli / Sam) pour faciliter les tests manuels.
import { randomUUID } from "node:crypto";
import { db } from "./db.js";

db.exec("DELETE FROM alerts; DELETE FROM bonus_grants; DELETE FROM instant_commands; DELETE FROM activity_snapshots; DELETE FROM devices; DELETE FROM children; DELETE FROM pairing_codes;");

function insertChild({ name, avatarColor, defaultConfig }) {
  const id = randomUUID();
  db.prepare("INSERT INTO children (id, name, avatar_color, default_config) VALUES (?, ?, ?, ?)").run(
    id,
    name,
    avatarColor,
    JSON.stringify(defaultConfig)
  );
  return id;
}

function insertDevice(childId, { name, model, overrides, online, usedMinutes, topSites, topApps }) {
  const id = randomUUID();
  db.prepare(
    "INSERT INTO devices (id, child_id, name, model, overrides, online, paired_at) VALUES (?, ?, ?, ?, ?, ?, datetime('now'))"
  ).run(id, childId, name, model, JSON.stringify(overrides), online ? 1 : 0);

  db.prepare(
    "INSERT INTO activity_snapshots (device_id, date, used_minutes, top_sites, top_apps) VALUES (?, date('now'), ?, ?, ?)"
  ).run(id, usedMinutes, JSON.stringify(topSites), JSON.stringify(topApps));

  return id;
}

const eliId = insertChild({
  name: "Eli",
  avatarColor: "#7c9cff",
  defaultConfig: {
    time: { dailyQuotaMinutes: 90, windowStart: "09:00", windowEnd: "20:00" },
    web: { supervisionOn: true, whitelistMode: false, blacklist: ["tiktok.com", "roblox.com/chat"], whitelist: [] },
    apps: {
      supervisionOn: true,
      limits: [{ id: "l1", apps: ["YouTube Kids"], minutesPerDay: 45 }],
      blockedApps: ["TikTok"],
    },
  },
});

insertDevice(eliId, {
  name: "Tablette Eli",
  model: "Lenovo Tab M10",
  overrides: {},
  online: true,
  usedMinutes: 42,
  topSites: [["youtube.com", 8], ["duolingo.com", 5], ["google.com", 2]],
  topApps: [{ name: "YouTube Kids", minutes: 25 }, { name: "Duolingo", minutes: 12 }],
});

insertDevice(eliId, {
  name: "PC Chambre Eli",
  model: "Windows 11 — portable",
  overrides: { time: { dailyQuotaMinutes: 60 } },
  online: false,
  usedMinutes: 15,
  topSites: [["netflix.com", 3]],
  topApps: [{ name: "Chrome", minutes: 15 }],
});

const samId = insertChild({
  name: "Sam",
  avatarColor: "#5ec8a8",
  defaultConfig: {
    time: { dailyQuotaMinutes: 120, windowStart: "10:00", windowEnd: "18:00" },
    web: { supervisionOn: true, whitelistMode: false, blacklist: [], whitelist: [] },
    apps: {
      supervisionOn: true,
      limits: [
        { id: "l1", apps: ["Instagram", "X"], minutesPerDay: 30 },
        { id: "l2", apps: ["WhatsApp"], minutesPerDay: 25 },
        { id: "l3", apps: ["Mail", "Spark"], minutesPerDay: 15 },
      ],
      blockedApps: [],
    },
  },
});

insertDevice(samId, {
  name: "PC-Salon",
  model: "Sam SM-A125F",
  overrides: {
    time: { dailyQuotaMinutes: 60, windowStart: "16:00", windowEnd: "19:00" },
    web: { whitelistMode: true, whitelist: ["wikipedia.org", "scolarite.education.fr"] },
  },
  online: false,
  usedMinutes: 168,
  topSites: [["gmail.com", 6], ["youtube.com", 6], ["wikipedia.org", 5], ["outlook.com", 4], ["google.com", 2]],
  topApps: [
    { name: "Gmail", minutes: 41 },
    { name: "YouTube", minutes: 20 },
    { name: "Norton Family", minutes: 7 },
    { name: "VLC", minutes: 6 },
    { name: "Chrome", minutes: 5 },
  ],
});

insertDevice(samId, {
  name: "Laptop Sam",
  model: "MacBook Air (à venir)",
  overrides: {},
  online: true,
  usedMinutes: 30,
  topSites: [["google.com", 4]],
  topApps: [{ name: "Safari", minutes: 30 }],
});

db.prepare("INSERT INTO alerts (id, device_id, text, severity) VALUES (?, NULL, ?, ?)").run(
  randomUUID(),
  "PC-Salon (Sam) hors ligne depuis 2h de façon inattendue",
  "warning"
);

console.log("Seed terminé : 2 enfants, 4 appareils.");
