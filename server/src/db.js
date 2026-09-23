import { DatabaseSync } from "node:sqlite";
import { mkdirSync } from "node:fs";
import { dirname } from "node:path";

const DB_PATH = process.env.DB_PATH || "./data/nautilyou.db";
mkdirSync(dirname(DB_PATH), { recursive: true });

export const db = new DatabaseSync(DB_PATH);
db.exec("PRAGMA journal_mode = WAL;");
db.exec("PRAGMA foreign_keys = ON;");

db.exec(`
CREATE TABLE IF NOT EXISTS children (
  id TEXT PRIMARY KEY,
  name TEXT NOT NULL,
  avatar_color TEXT NOT NULL DEFAULT '#7c9cff',
  default_config TEXT NOT NULL,
  created_at TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS devices (
  id TEXT PRIMARY KEY,
  child_id TEXT NOT NULL REFERENCES children(id) ON DELETE CASCADE,
  name TEXT NOT NULL,
  model TEXT,
  public_key TEXT,
  overrides TEXT NOT NULL DEFAULT '{}',
  online INTEGER NOT NULL DEFAULT 0,
  last_seen_at TEXT,
  paired_at TEXT,
  created_at TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS pairing_codes (
  code TEXT PRIMARY KEY,
  child_id TEXT NOT NULL REFERENCES children(id) ON DELETE CASCADE,
  device_name TEXT,
  expires_at TEXT NOT NULL,
  used_at TEXT,
  created_at TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS activity_snapshots (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  device_id TEXT NOT NULL REFERENCES devices(id) ON DELETE CASCADE,
  date TEXT NOT NULL,
  used_minutes INTEGER NOT NULL DEFAULT 0,
  top_sites TEXT NOT NULL DEFAULT '[]',
  top_apps TEXT NOT NULL DEFAULT '[]',
  blocked_apps_count INTEGER NOT NULL DEFAULT 0,
  created_at TEXT NOT NULL DEFAULT (datetime('now')),
  UNIQUE(device_id, date)
);

-- Applications installees sur l'appareil (envoyees par le client), proposees dans le dashboard
-- pour choisir les apps a limiter ou bloquer sans avoir a connaitre leur nom exact.
CREATE TABLE IF NOT EXISTS device_apps (
  device_id TEXT PRIMARY KEY REFERENCES devices(id) ON DELETE CASCADE,
  apps TEXT NOT NULL DEFAULT '[]',
  updated_at TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS bonus_grants (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  device_id TEXT NOT NULL REFERENCES devices(id) ON DELETE CASCADE,
  date TEXT NOT NULL,
  minutes INTEGER NOT NULL,
  created_at TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS instant_commands (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  device_id TEXT NOT NULL REFERENCES devices(id) ON DELETE CASCADE,
  type TEXT NOT NULL,
  payload TEXT NOT NULL DEFAULT '{}',
  delivered_at TEXT,
  created_at TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS alerts (
  id TEXT PRIMARY KEY,
  device_id TEXT REFERENCES devices(id) ON DELETE CASCADE,
  text TEXT NOT NULL,
  severity TEXT NOT NULL DEFAULT 'info',
  read INTEGER NOT NULL DEFAULT 0,
  created_at TEXT NOT NULL DEFAULT (datetime('now'))
);

-- Demandes envoyées par l'enfant depuis le client (bonus temps, accès à un site bloqué),
-- affichées dans l'onglet Alertes du dashboard pour approbation/refus par le parent.
CREATE TABLE IF NOT EXISTS requests (
  id TEXT PRIMARY KEY,
  device_id TEXT NOT NULL REFERENCES devices(id) ON DELETE CASCADE,
  type TEXT NOT NULL, -- 'time' | 'site'
  payload TEXT NOT NULL DEFAULT '{}',
  status TEXT NOT NULL DEFAULT 'pending', -- 'pending' | 'approved' | 'denied'
  created_at TEXT NOT NULL DEFAULT (datetime('now')),
  resolved_at TEXT
);
`);

// Migration légère (le 2026-09-23) : activity_snapshots avait une colonne top_categories jamais
// réellement alimentée par le client (aucune catégorisation de site n'a jamais existé) ;
// remplacée par top_sites (vrais domaines visités, dérivés du filtre DNS). CREATE TABLE IF NOT
// EXISTS ne touche pas une table déjà existante, d'où cet ADD COLUMN explicite pour les bases
// déjà créées avant ce changement.
try {
  db.exec("ALTER TABLE activity_snapshots ADD COLUMN top_sites TEXT NOT NULL DEFAULT '[]'");
} catch {
  // colonne déjà présente (base créée après ce changement, ou migration déjà appliquée)
}
