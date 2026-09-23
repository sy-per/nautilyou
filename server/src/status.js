// Statut "vert/rouge" d'un appareil : vert seulement si les conditions ACTIVES sont vraies
// - si quotaEnabled : il reste du temps de quota aujourd'hui (quota + bonus - utilisé > 0)
// - si windowEnabled : l'heure actuelle est dans la plage horaire autorisée
// Un mode désactivé (quotaEnabled=false ou windowEnabled=false) ne bloque jamais, par définition.
export function computeDeviceStatus(config, usedMinutesToday, bonusMinutesToday) {
  const { quotaEnabled = true, windowEnabled = true, dailyQuotaMinutes, windowStart, windowEnd } = config.time;
  const quota = dailyQuotaMinutes + bonusMinutesToday;
  const remainingMinutes = quota - usedMinutesToday;
  const hhmm = new Date().toTimeString().slice(0, 5);
  const withinWindow = windowStart <= hhmm && hhmm <= windowEnd;

  const quotaOk = !quotaEnabled || remainingMinutes > 0;
  const windowOk = !windowEnabled || withinWindow;

  return {
    remainingMinutes,
    withinWindow,
    quotaEnabled,
    windowEnabled,
    ok: quotaOk && windowOk,
  };
}
