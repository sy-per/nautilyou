// Listes publiques proposees pour le filtre enfant. Les identifiants doivent rester identiques a ceux de
// BlocklistCatalog (client Windows, BlocklistStore.cs) : c'est le client qui telecharge chaque liste.
export const BLOCKLISTS = [
  {
    id: "ut1-adult",
    name: "UT1 — Université Toulouse Capitole (adulte)",
    description: "Référence française, ~4,6 millions de domaines. Recommandée.",
    license: "CC BY-SA 4.0",
    recommended: true,
  },
  {
    id: "blp-porn",
    name: "The Block List Project — pornographie",
    description: "~950 000 domaines, mise à jour régulière.",
    license: "domaine public",
  },
  {
    id: "stevenblack-porn",
    name: "StevenBlack — pornographie",
    description: "~77 000 domaines, liste compacte.",
    license: "MIT",
  },
  {
    id: "hagezi-nsfw",
    name: "Hagezi — contenu adulte (NSFW)",
    description: "~75 000 domaines, très fréquemment mise à jour.",
    license: "GPL-3.0",
  },
  {
    id: "cloudflare-family",
    name: "DNS famille Cloudflare (1.1.1.3)",
    description: "Bloque contenu adulte et sites malveillants, sans liste à télécharger.",
    license: "service gratuit",
  },
];

export const DEFAULT_CHILD_FILTER = { enabled: false, lists: ["ut1-adult"] };

// Les configurations enregistrées avant l'ajout du filtre enfant n'ont pas ce champ.
export function childFilterOf(web) {
  return web.childFilter ?? DEFAULT_CHILD_FILTER;
}
