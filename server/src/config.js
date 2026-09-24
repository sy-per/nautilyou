// Fusionne la config par défaut d'un enfant avec les overrides d'un appareil, champ par champ,
// même logique que resolveConfig() côté dashboard-ui (à garder synchronisées).
export function resolveConfig(defaultConfig, overrides) {
  const o = overrides || {};
  return {
    time: { ...defaultConfig.time, ...(o.time || {}) },
    web: { ...defaultConfig.web, ...(o.web || {}) },
    apps: { ...defaultConfig.apps, ...(o.apps || {}) },
  };
}

export function hasOverride(overrides, section) {
  return Boolean(overrides && overrides[section]);
}

export function defaultConfigTemplate() {
  return {
    time: {
      quotaEnabled: true,
      dailyQuotaMinutes: 120,
      windowEnabled: true,
      windowStart: "09:00",
      windowEnd: "20:00",
    },
    // childFilter : filtre enfant (contenu adulte) fonde sur des listes publiques, choisies dans le
    // dashboard (identifiants : voir dashboard-ui/src/blocklists.js). En mode liste noire, la liste
    // blanche sert d'exceptions a ces listes.
    web: {
      supervisionOn: true,
      whitelistMode: false,
      blacklist: [],
      whitelist: [],
      childFilter: { enabled: false, lists: ["ut1-adult"] },
    },
    apps: {
      supervisionOn: true,
      // Limites indépendantes façon "Temps d'écran" iOS : chaque règle a son propre groupe
      // d'apps et sa propre limite quotidienne, ajoutables/supprimables librement.
      limits: [], // [{ id, apps: string[], minutesPerDay: number }]
      blockedApps: [],
    },
  };
}
