// Date du jour dans le fuseau horaire du serveur (variable TZ), au format AAAA-MM-JJ : la meme journee
// que celle du client Windows, qui compte le temps par jour local. (toISOString donnerait la date UTC,
// decalee de 1 a 2 h : le "jour" changerait a 1 h ou 2 h du matin au lieu de minuit.)
export function today() {
  const d = new Date();
  const pad = (n) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}
