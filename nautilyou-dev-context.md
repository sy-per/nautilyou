# Nautilyou — contexte de développement

> Fichier de suivi source de vérité pour ce projet. À lire/mettre à jour à chaque session au lieu de relire toute la conversation.

## Pitch

Application de contrôle parental **selfhosted** : gestion du temps d'écran, gestion des sites accessibles, dashboard de pilotage pour les parents.

- **Serveur** : conteneur Docker (dashboard + API), installé par le parent (NAS, VPS perso, PC maison...).
- **Client** : agent Windows (V1), puis Linux/macOS plus tard. Tourne en arrière-plan sur le PC de l'enfant, applique les règles localement.
- **Communication client ↔ serveur** : **WebSocket persistant et authentifié** entre le client et le serveur du parent (le sien, pas un tiers) pour échanger la configuration (règles) et les rapports d'usage en temps réel — voir "Décision P2P → WebSocket" ci-dessous. Doit fonctionner **même quand le PC de l'enfant n'est pas sur le réseau local** (école, ami, 4G...), ce qui suppose que le serveur soit joignable depuis internet (port forwarding / reverse proxy / DDNS, à la charge du parent qui l'auto-héberge).

## Décisions prises (session du 2026-09-23)

| Sujet | Décision | Raison |
|---|---|---|
| Portée réseau | Le client doit rester joignable hors LAN | L'enfant part avec le PC, le contrôle parental doit continuer de s'appliquer et de remonter les infos |
| Stack client | C#/.NET | Natif Windows, accès bas niveau (WFP, hooks process/fenêtre), service Windows robuste, portable vers Linux/macOS via .NET cross-platform plus tard |
| Stack serveur | Node.js (Express/Fastify) + React | Bon support WebRTC/signaling côté JS, facile à dockeriser, cohérent avec les autres projets (MakeInWeb, Mail Junior) |
| ~~P2P hors LAN~~ **Remplacé le 2026-09-23** | ~~WebRTC DataChannel + signaling + STUN/TURN~~ → **WebSocket persistant et authentifié client↔serveur** | Le serveur doit de toute façon être joignable depuis internet pour le signaling initial, donc le P2P n'évite pas cette contrainte ; et "l'intermédiaire" que le P2P cherche à éviter est ici le propre serveur self-hosted du parent, pas un tiers — bypasser son propre serveur n'a pas de bénéfice de confidentialité. Le WebSocket est radicalement plus simple (pas d'ICE/STUN/TURN/SDP, pas de lib WebRTC côté .NET) pour un résultat tout aussi réactif. Voir section "Canal de données WebSocket" plus bas. |
| Fail-safe hors ligne | Le client récupère la config quand il a du réseau, puis l'**applique telle quelle tant qu'il est hors réseau et que la config n'a pas changé côté serveur** (dernière config connue = source de vérité locale, pas de blocage total ni de passage en mode libre par défaut) | Le contrôle parental doit continuer à s'appliquer normalement même sans connexion (trajet, coupure réseau...), sans pénaliser l'enfant par un blocage total injustifié |
| Règles de temps | Deux mécanismes combinables : **quota quotidien** (ex. 2h/jour) ET **plage horaire autorisée** (ex. 10h→18h) | Le parent doit pouvoir limiter le volume total ET la fenêtre de la journée où l'écran est utilisable (ex. pas d'écran avant 10h ni après 18h, même si le quota n'est pas atteint) |
| Gestion des sites | Liste noire (sites interdits) ET liste blanche (sites autorisés, mode restrictif) gérées explicitement dans le dashboard, indépendamment des catégories | La capture d'inspiration ne montre pas cette gestion (seulement des catégories/stats) mais c'est un impératif explicite du produit, pas une option secondaire |

## Modèle de données (hiérarchie)

Impératif du 2026-09-23 : gestion côté serveur des **enfants**, et pour chaque enfant, gestion de ses **appareils** — plusieurs enfants, chacun pouvant avoir plusieurs appareils, chaque appareil pouvant avoir une **configuration différente**.

```
Famille (compte parent, 1 par instance self-hosted en V1)
 └─ Enfant (profil) — a une config par défaut (temps d'écran, sites)
     └─ Appareil (device) — appairé individuellement, config propre
```

- **Enfant** : profil (nom, photo/avatar optionnel), porte une **config par défaut** appliquée à tous ses appareils sauf override.
- **Appareil** : rattaché à un seul enfant, identité propre issue du pairing (clé publique + id), statut en ligne/hors ligne, sa propre config.
- **Résolution de la config effective d'un appareil** = config par défaut de l'enfant **surchargée** champ par champ par les overrides définis sur cet appareil précis (ex : l'enfant a une limite de 2h/jour par défaut, mais son PC fixe du salon a une limite spécifique de 1h en semaine ; sa tablette garde le défaut).
- Un enfant peut donc avoir un PC avec des règles strictes et une tablette avec des règles différentes, gérés indépendamment depuis le même profil enfant.
- Dashboard : navigation à deux niveaux — liste des enfants → détail enfant (config par défaut + liste de ses appareils) → détail appareil (overrides + statut + historique).

## Inspiration UI/UX dashboard (référence visuelle du 2026-09-23)

Capture d'écran fournie par l'utilisateur (type Norton Family / Qustodio) comme direction pour le dashboard serveur. Éléments à retenir :

- **Barre de sélection enfant en haut** : un avatar rond par enfant (+ badge "Alerts" avec compteur de notifications non lues, + bouton "Add Child"). Cliquer sur un enfant filtre tout le contenu en dessous → correspond à notre navigation "liste enfants → détail enfant".
- **Barre d'actions rapides** en haut du détail enfant : actions instantanées type "Instant School Time" (applique immédiatement un mode restreint) et "Instant Lock" (verrouille tout de suite), + liens "Add Device", "View Profile", et un toggle global "Supervision" (on/off) pour l'enfant sélectionné.
- **Sélecteur d'appareil** (dropdown, ex : "Sam SM-A125F") pour basculer l'"Activity Summary" d'un appareil à l'autre → correspond à notre navigation "détail enfant → détail appareil".
- **Activity Summary (derniers 7 jours)** en 3 colonnes, chacune avec son propre toggle on/off et son icône de réglages (⚙) :
  - **WEB** : niveau de supervision (ex "Block"), catégories les plus actives avec compteur (General, Entertainment, Reference/Educ..., Business, Search...).
  - **TIME** : total d'heures autorisées, jauge circulaire "X/Y HOURS USED".
  - **APP** : nombre d'apps bloquées, liste des apps les plus utilisées avec icône, appareil source, barre de progression et durée.

**À retenir pour notre archi/plan** :
- Le toggle "Supervision" global par enfant + toggles par catégorie (WEB/TIME/APP) confirme le besoin de pouvoir activer/désactiver une catégorie de règle indépendamment (pas juste un on/off global).
- Les "actions instantanées" (Instant Lock, Instant School Time) sont un besoin UX à ajouter à la roadmap : au-delà des règles planifiées, le parent doit pouvoir pousser un override immédiat et temporaire depuis le dashboard (transite par le même canal P2P que la config).
- Le dropdown appareil + résumé d'activité par appareil confirme la structure Enfant → Appareils du modèle de données déjà défini.
- Cette référence guidera le design du dashboard React en Phase 1/2 (pas de code UI pour l'instant, on reste en plan).

## Architecture cible

### Serveur (Docker Compose)

- **App principale** (Node.js/Express + React) : API REST + dashboard web.
  - Sert aussi de **canal WebSocket** (`/ws/device`) pour la synchronisation temps réel avec chaque appareil (push de config, commandes instantanées, remontée d'activité) — voir "Décision P2P → WebSocket" ci-dessous.
- **Base de données** : SQLite (simplicité selfhost mono-conteneur) — migration vers Postgres envisageable si besoin multi-instance plus tard.

### Client Windows (C#/.NET)

- **Service Windows** (tourne en SYSTEM, démarre avant l'ouverture de session, résistant à la fermeture par un utilisateur non-admin).
- **Moteur d'application locale** :
  - Suivi du temps d'écran (process/fenêtre active).
  - Blocage de sites : filtrage réseau local (Windows Filtering Platform et/ou proxy/DNS local).
  - Verrouillage de session quand le temps est écoulé.
  - **Cache de config chiffré en local** : l'appareil pull la config dès qu'il a du réseau, puis l'applique tel quel en continu hors ligne — la dernière config reçue reste active jusqu'à la prochaine reconnexion **et** un changement effectif côté serveur (pas de retour à un mode libre, pas de blocage total par défaut).
  - **Moteur de règles temps** : combine quota quotidien (ex. 2h/jour cumulées) et plage horaire autorisée (ex. écran utilisable seulement entre 10h et 18h) — les deux conditions doivent être satisfaites pour autoriser l'usage.
- **Module de sync temps réel** : le client garde une connexion **WebSocket persistante** avec le serveur (reconnexion automatique si coupée) pour :
  - Recevoir la config à jour instantanément (push serveur→client dès qu'un parent modifie un réglage).
  - Recevoir les commandes instantanées (verrouillage, bonus) sans délai.
  - Pousser les rapports d'usage au fil de l'eau (repli HTTP REST si la connexion WebSocket est momentanément coupée).
- **Pairing/enrollment** : code d'appairage généré dans le dashboard, saisi une fois dans le client → échange de clés (type trust-on-first-use façon WireGuard) pour authentifier les connexions WebSocket suivantes.
- **UI minimale enfant** : icône systray, temps restant visible. PIN parent pour override/pause temporaire.

### Sécurité

- Pairing initial protégé par code à usage unique + échange de clés asymétriques (pas de secret partagé en clair) — **actuellement simplifié en V1** : la clé transmise au pairing est un GUID aléatoire, pas une vraie paire asymétrique ; à corriger avant tout déploiement réel.
- La connexion WebSocket ré-authentifie l'appareil à chaque connexion via son identifiant + sa clé de pairing.
- Le trafic doit être chiffré via TLS/WSS en production (reverse proxy avec certificat, à documenter en Phase 3/déploiement).
- Service client protégé contre désinstallation/arrêt par un compte non-admin.

## Fonctionnalités dashboard (V1+)

- Gestion multi-enfants : créer/éditer/supprimer un profil enfant avec sa config par défaut.
- Gestion multi-appareils par enfant : appairer un nouvel appareil, définir des overrides de config propres à cet appareil, voir lequel hérite du défaut vs lequel a des règles spécifiques.
- Règles de temps d'écran : quota quotidien (ex. 2h/jour) **et** plage horaire autorisée (ex. 10h→18h), combinables, définissables au niveau enfant (défaut) ou au niveau appareil (override).
- Règles de sites : gestion explicite **liste noire** (sites interdits) et **liste blanche** (sites autorisés, mode restrictif) — indépendant du filtrage par catégories (V2) — même logique défaut enfant / override appareil.
- Statut live : appareil en ligne/hors ligne, temps restant aujourd'hui.
- Actions instantanées : verrouillage immédiat ("Instant Lock") et **bonus temps** (+15min/+30min/+1h ajoutés au quota du jour, poussés en P2P). ("Mode école instantané" écarté — pas de besoin identifié.)
- Toggle de supervision par catégorie (WEB / TEMPS / APPS) — chaque catégorie de règle peut être activée/désactivée indépendamment. Pas de toggle "supervision" global par enfant : un appareil appairé est forcément supervisé.
- Rapports : historique d'usage, sites visités, répartition du temps.
- Alertes : appareil hors ligne de façon inattendue, tentative de contournement détectée.

## Roadmap

- **Phase 0 — Idéation & plan** (en cours, ce document)
- **Phase 1 — MVP** : modèle de données multi-enfants/multi-appareils dès le départ (même si testé avec 1 enfant / 1 appareil en pratique), client Windows avec temps d'écran uniquement, dashboard basique — **fait**, avec WebSocket au lieu de P2P (cf. décision du 2026-09-23)
- **Phase 2** : blocage de sites (filtrage réseau) — hosts file fait pour la liste noire, WFP/proxy pour la liste blanche reste à faire
- **Phase 3 (redéfinie)** : rendre le serveur joignable depuis internet pour un usage hors LAN (reverse proxy + TLS/WSS + doc pour port forwarding/DDNS chez le parent) — plus besoin de NAT traversal côté client puisque c'est le client qui initie la connexion WebSocket sortante vers le serveur, pas l'inverse
- **Phase 4** : rapports avancés, UX multi-enfants/multi-appareils poussée (agrégation, comparaison), alertes
- **Phase 5** : client Linux/macOS

## Questions ouvertes / à trancher plus tard

- Liste noire/blanche : saisie manuelle par domaine uniquement en V1, ou import/suggestions (listes communautaires) dès le départ ?
- Si liste blanche active : comportement par défaut pour tout ce qui n'est ni dans la whitelist ni dans la blacklist (bloqué puisque whitelist = mode restrictif, à confirmer) ?
- Filtrage par catégories (V2, cf. capture d'inspiration) : nécessite une liste/API de classification à choisir plus tard.
- Multi-appareils par enfant : agrégation du temps d'écran entre plusieurs PCs/appareils ?
- Notification parent en temps réel (push, email) en cas de dépassement ou tentative de contournement ?

## Séquence de développement décidée (2026-09-23)

Ordre validé : **UI mock (mock data, pas de backend) → serveur (Docker, API, DB) → client Windows**.
Raison : valider l'ergonomie vite et à moindre coût côté React ; le client aura de toute façon besoin d'un serveur réel pour tester pairing/P2P, donc le serveur doit exister avant le client. Le client Windows (C#/.NET/WFP) est la brique la plus lourde, en dernier.

## UI mock — dashboard (Phase 1, en cours)

- Emplacement : `Nautilyou/dashboard-ui/` — app Vite + React, `npm install` puis `npm run dev` (port 5183). Lançable aussi via `preview_start` avec le nom `nautilyou-dashboard-ui` (`.claude/launch.json` à la racine `Claude code/`).
- Données mock dans `dashboard-ui/src/data/mockData.js` : reproduit fidèlement la hiérarchie Famille → Enfant (config par défaut) → Appareil (overrides), avec `resolveConfig()` qui fusionne défaut + overrides champ par champ, et `hasOverride()` pour afficher un badge "override appareil" dans l'UI quand un appareil diffère du défaut.
- Écran unique (`App.jsx`) validé visuellement : barre enfants (avatars + alertes + ajout), action instantanée (verrouillage uniquement), sélecteur d'appareil, 3 cartes togglables (WEB / TEMPS / APPS) inspirées de la capture Norton Family, + panneau **Listes de sites** (liste noire / liste blanche) qui manquait dans la référence.
- **Retiré le 2026-09-23** : "Mode école instantané" (pas de besoin identifié) et le toggle "Supervision" global par enfant — un appareil appairé/enrôlé est **forcément supervisé** (pas de mode "non supervisé" possible), donc ce switch n'a pas de sens. Les toggles par catégorie (WEB/TEMPS/APPS) restent : ils activent/désactivent un *type de règle*, pas la supervision elle-même.
- Volontairement non câblé (mock only) : boutons "Ajouter enfant/appareil", verrouillage instantané, toggles de config, boutons de suppression de site → tous visuels, pas de logique métier ni de persistance. Exception : le **bonus temps** est câblé en local (state React) pour valider l'interaction — cliquer +15/+30/+60min augmente réellement le quota affiché et la jauge de la carte TEMPS de l'appareil sélectionné (pas de persistance serveur, réinitialisé au rechargement).
- Cas testés visuellement : Eli (pas d'override, tout hérite du défaut) vs Sam/PC-Salon (override quota+plage horaire+passage en liste blanche) → les badges "override appareil" et le mode liste blanche s'affichent correctement.

## Serveur — API + DB (Phase suivante, en cours)

- Emplacement : `Nautilyou/server/` — Node.js (ESM) + Express, port par défaut `4100`.
- **DB** : SQLite via le module natif **`node:sqlite`** (`DatabaseSync`, stable dans Node 24, aucune dépendance native à compiler — testé et fonctionnel). Fichier DB dans `server/data/nautilyou.db` (gitignored), configurable via `DB_PATH`.
- Schéma (`src/db.js`) : `children`, `devices`, `pairing_codes`, `activity_snapshots`, `bonus_grants`, `instant_commands`, `alerts` — reflète le modèle Famille → Enfant (default_config JSON) → Appareil (overrides JSON).
- `src/config.js` : `resolveConfig()` fusionne default_config + overrides champ par champ — **même logique que `resolveConfig()` côté `dashboard-ui`, à garder synchronisées si l'une évolue**.
- Routes testées et fonctionnelles (curl) :
  - `GET/POST /api/children`, `GET/PUT /api/children/:id`, `PUT /api/children/:id/config`, `DELETE /api/children/:id`
  - `POST /api/children/:id/pairing-code` → génère un code à 6 chiffres (15 min de validité)
  - `POST /api/pairing/complete` → le client échange le code + sa clé publique contre un appareil créé (device paired, `online=1`)
  - `GET /api/devices/:id` → détail complet avec `config` résolue, `hasOverride` par section, `bonusMinutesToday`, activité du jour
  - `PUT /api/devices/:id/overrides`, `DELETE /api/devices/:id`
  - `POST /api/devices/:id/lock` → **[remplacé, voir section "Décision P2P → WebSocket"]** pousse maintenant la commande en direct via WebSocket si l'appareil est connecté (repli en file d'attente `instant_commands` sinon)
  - `POST /api/devices/:id/bonus` → ajoute des minutes au quota du jour (`bonus_grants`) + pousse la config à jour en direct via WebSocket
  - `POST /api/devices/:id/activity` → repli HTTP si le WebSocket est déconnecté (upsert par jour) ; en fonctionnement normal, l'activité arrive via le WebSocket (voir `deviceSocket.js`)
  - `GET /api/devices/:id/activity?days=7`
  - `GET /api/alerts`, `POST /api/alerts/:id/read`
- ~~Signaling WebRTC (`src/signaling.js`)~~ **remplacé le 2026-09-23** par `src/deviceSocket.js` : WebSocket sur `/ws/device?deviceId=...&token=...`, canal de données authentifié (pas juste un relai SDP) — voir section dédiée plus bas.
- `src/seed.js` : rejoue les mêmes données que le mock UI (Eli/Sam + leurs 4 appareils) pour tester l'API facilement (`npm run seed`).
- **Docker** : `server/Dockerfile` (node:22-alpine, aucune compilation native grâce à `node:sqlite`) + `Nautilyou/docker-compose.yml` (service `server` exposé sur `4100`, volume `nautilyou_data` pour la DB). **Testé avec succès le 2026-09-23** (voir section dédiée plus bas — build, run, persistance, WebSocket, tout fonctionne). Le service `turn` optionnel mentionné dans une version précédente de ce document n'est plus pertinent (plus de WebRTC).
- **Impératif noté le 2026-09-23** : le dashboard aura une **page de connexion** pour accéder à l'interface serveur — volontairement **repoussée en tout dernier** dans l'implémentation pour garder un accès libre à l'API/UI pendant tout le développement. Pas d'auth implémentée à ce stade.

### Pas encore fait côté serveur
- Authentification/page de connexion (volontairement en dernier, cf. ci-dessus).
- Test réel en conteneur Docker (build + compose up) — testé en local (`node src/index.js`) uniquement pour l'instant.
- Vraie logique de relai des `instant_commands` vers un client (n'existe pas encore).

## Dashboard branché sur l'API réelle (2026-09-23)

- `dashboard-ui` ne dépend plus de données mock (fichier `src/data/mockData.js` supprimé). Nouveau module `src/api.js` (fetch simple, base URL `VITE_API_URL` ou `http://localhost:4100/api` par défaut) : `fetchChildren`, `fetchDevice`, `postBonus`, `postLock`.
- `App.jsx` charge la liste des enfants au montage (`GET /api/children`), puis le détail complet de l'appareil sélectionné (`GET /api/devices/:id`, qui inclut déjà `config` résolue + `hasOverride` + activité + `bonusMinutesToday` — pas de logique de fusion côté client).
- **Bonus temps** est maintenant réellement persisté côté serveur (`POST /api/devices/:id/bonus`) plutôt que du state React local — testé dans le navigateur : cliquer "+15min" met à jour le quota affiché ET la ligne `bonus_grants` en base.
- **Verrouillage instantané** appelle `POST /api/devices/:id/lock` et affiche une confirmation éphémère ("Commande de verrouillage envoyée") ; la commande est stockée dans `instant_commands` mais rien ne la délivre encore à un client (n'existe pas).
- Testé dans le navigateur : bascule Eli/Sam, bascule d'appareil (badges "override appareil" corrects), bonus temps persistant en base — tout fonctionne avec les 2 serveurs (API sur `4100`, Vite sur `5183`) tournant en parallèle.
- Pour lancer les deux ensemble en dev : `node server/src/index.js` (ou `npm run dev` dans `server/`) + `npm run dev` dans `dashboard-ui/` (ou via le `.claude/launch.json` du dépôt, nom `nautilyou-dashboard-ui`).

## Modules de configuration + badge statut + bonus libre (2026-09-23)

Impératifs ajoutés par l'utilisateur, tous implémentés et testés dans le navigateur :

- **Bouton ⚙ sur chaque carte (WEB/TEMPS/APPS)** : ouvre un modal de configuration pour l'**appareil sélectionné** (écrit dans `overrides` de ce device précis).
  - **WEB** : choix du mode (liste noire / liste blanche) + édition des deux listes (remplace les boutons "+ajouter/retirer" statiques du panneau `Listes de sites`, qui est redevenu un simple panneau d'affichage renvoyant vers ce bouton ⚙).
  - **TEMPS** : quota quotidien (minutes) + plage horaire (heure début/fin).
  - **APPS** : nouveau modèle de config plus riche (`server/src/config.js` → `defaultConfigTemplate().apps`) :
    ```
    apps: { supervisionOn, mode: 'limit_selected' | 'limit_all_except_selected', selectedApps: [...], limitMinutes, blockedApps: [...] }
    ```
    Le modal permet de choisir les apps concernées, le mode (limiter seulement la sélection vs limiter tout sauf la sélection), la limite en minutes, et la liste des apps totalement bloquées.
- **Bouton "⚙ Paramètres (tous les appareils)"** à côté de "Voir le profil" : ouvre le **même modal mais avec les 3 onglets** (Web/Temps/Apps) et édite la **config par défaut de l'enfant** (`PUT /api/children/:id/config`) — s'applique à tous ses appareils sauf ceux qui ont un override spécifique. Composant partagé `dashboard-ui/src/ConfigModal.jsx`.
- **Bonus temps avec saisie libre** : en plus des presets +15/+30/+60min, un champ numérique + bouton "Ajouter" permet de saisir n'importe quelle valeur en minutes (`BonusTimeButton` dans `App.jsx`).
- **Badge vert/rouge sur l'avatar enfant** : petit point coloré en bas à droite de l'avatar.
  - **Vert** = au moins un des appareils de l'enfant satisfait les 2 conditions : il reste du temps de quota aujourd'hui (quota + bonus − utilisé > 0) **et** l'heure actuelle est dans sa plage horaire autorisée.
  - **Rouge** = aucun appareil ne satisfait les 2 conditions.
  - Pas de badge si l'enfant n'a aucun appareil.
  - **Confirmé par l'utilisateur le 2026-09-23** : agrégation sur tous les appareils de l'enfant, vert si au moins un appareil a accès (temps restant + dans la plage horaire). Logique déjà implémentée telle quelle depuis le départ, pas de changement de code nécessaire.
  - Calcul fait côté serveur : `server/src/status.js` (`computeDeviceStatus`), exposé par `GET /api/children` (champ `status` par enfant et par appareil) et par `GET /api/devices/:id`.
- Testé dans le navigateur : changement de mode WEB → badge "override appareil" apparaît, bonus personnalisé (+22min) reflété immédiatement dans la jauge, modal enfant avec tabs fonctionnel.

### Switches d'activation par mode (ajouté le 2026-09-23, suite à retour utilisateur)

Chaque modal de config a maintenant des switches pour activer/désactiver indépendamment ses sous-parties, pas juste éditer leurs valeurs :

- **TEMPS** : switch "Quota quotidien actif" + switch "Plage horaire active", indépendants. Si désactivé, ce champ affiche "illimité"/"toute la journée" dans la carte et n'est plus pris en compte dans le calcul du statut (`server/src/status.js`). Nouveau champs `time.quotaEnabled` / `time.windowEnabled` (booléens, défaut `true` via fallback si absent — rétrocompatible avec les configs existantes).
- **WEB** et **APPS** : switch maître "Supervision activée" en haut du modal (relié au champ `supervisionOn` déjà existant), grise le reste du formulaire quand désactivé.
- Les toggles dans l'en-tête de chaque carte (WEB/TEMPS/APPS) sont maintenant **fonctionnels** (avant : `onToggle={() => {}}`, factices) : ils appellent directement `PUT /api/devices/:id/overrides` pour un togglage rapide sans ouvrir le modal complet. Pour TEMPS, le toggle d'en-tête active/désactive quota ET plage horaire ensemble (pause rapide globale) ; le modal permet de les dissocier plus finement.
- Composant `Toggle` extrait dans `dashboard-ui/src/Toggle.jsx` (partagé entre `App.jsx` et `ConfigModal.jsx`).
- Testé dans le navigateur : désactivation de "Plage horaire active" pour Eli → carte affiche "toute la journée", badge avatar passe au vert (car statut réévalué sans la contrainte horaire).

### Refonte du modèle APPS : limites multiples façon iOS Temps d'écran (2026-09-23)

Suite à une référence visuelle (capture iOS Temps d'écran : "Instagram, X et 2 autres — 30 min, tous les jours"), le modèle APPS a été revu : ce ne sont **pas** un mode global + une liste + une seule limite, mais une **liste de règles indépendantes**, chacune avec son propre groupe d'apps et sa propre limite quotidienne, ajoutables/supprimables librement.

- Nouveau schéma (`server/src/config.js`) :
  ```
  apps: { supervisionOn, limits: [{ id, apps: string[], minutesPerDay }], blockedApps: [] }
  ```
  (remplace l'ancien `mode` + `selectedApps` + `limitMinutes` unique)
- `AppCard` (dashboard) affiche chaque règle avec un libellé condensé façon iOS : 1 app → son nom ; 2 apps → "A et B" ; 3+ → "A, B et N autres" (`formatAppsLabel()` dans `App.jsx`).
- Modal de config APPS (`ConfigModal.jsx`) : chaque règle est un bloc avec ses propres apps (tag editor) + son propre champ minutes + bouton "supprimer", et un bouton "+ Ajouter une limite" pour en créer une nouvelle vide. La liste "Apps totalement bloquées" reste séparée (blocage total indépendant des limites de temps).
- Seed mis à jour : Sam a maintenant 3 règles de démo (Instagram+X 30min, WhatsApp 25min, Mail+Spark 15min) reproduisant l'exemple de la capture.
- Testé dans le navigateur : ajout d'une 4ᵉ règle (Snapchat, 30min) via le modal → persistée en base et affichée correctement sur la carte APPS avec badge "override appareil".

## Gestion ajout/suppression d'enfant + "Tous les appareils" (2026-09-23)

Constat de l'utilisateur : contrairement à l'ajout/pairing d'un appareil (qui nécessite un vrai client), l'ajout/suppression d'un **enfant** est une opération purement côté serveur — pas besoin d'attendre le client Windows pour la câbler. Implémenté et testé dans le navigateur :

- **"Ajouter enfant"** (bouton dans la barre du haut) ouvre un modal (`dashboard-ui/src/ChildModal.jsx`) : prénom + couleur d'avatar (palette de 6 couleurs) → `POST /api/children`. L'enfant créé devient automatiquement sélectionné.
- **"Voir le profil"** ouvre le même modal en mode édition (nom/couleur modifiables via `PUT /api/children/:id`) + une zone "danger" avec confirmation en 2 temps pour **supprimer l'enfant** (`DELETE /api/children/:id`, cascade sur ses appareils côté DB). Après suppression, sélection automatique du premier enfant restant.
- **Bug latent corrigé au passage** : un enfant sans aucun appareil (cas d'un enfant tout juste créé) bloquait l'UI sur "Chargement…" indéfiniment (l'ancien code attendait un `deviceDetail` qui ne pouvait jamais arriver). Résolu par le changement décrit ci-dessous (entrée "Tous les appareils").

### Remplacement du bouton "Paramètres (tous les appareils)" par une entrée dans le sélecteur d'appareil

Sur suggestion de l'utilisateur : au lieu d'un bouton séparé ouvrant un modal à onglets, le sélecteur d'appareil a maintenant une option **"⚙ Tous les appareils (réglages par défaut)"** en tête de liste qui se comporte comme un appareil virtuel :

- Sélectionner cette entrée charge la **config par défaut de l'enfant** (`child.defaultConfig`) dans les mêmes cartes WEB/TEMPS/APPS, avec une activité vide (pas de vraie donnée agrégée) et jamais de badge "override appareil" (puisque c'est la source du défaut elle-même).
- Les boutons ⚙ de chaque carte ouvrent le **même modal single-section** que pour un appareil réel ; seule la cible d'enregistrement change : `PUT /api/children/:id/config` (defaultConfig) au lieu de `PUT /api/devices/:id/overrides`. Le modal affiche un petit chip "tous les appareils" dans son titre pour indiquer la cible.
- Les actions instantanées (verrouillage, bonus temps) sont désactivées quand "Tous les appareils" est sélectionné : ce sont des commandes envoyées en temps réel (WebSocket) à un appareil physique précis, ça n'a pas de sens sur l'entrée virtuelle.
- Un enfant **sans aucun appareil** affiche automatiquement cette vue par défaut (avec un message explicatif), ce qui permet de préconfigurer les réglages avant même le premier appairage.
- `ConfigModal.jsx` simplifié en conséquence : suppression du système d'onglets multiples (`tabs`/`activeTab`) devenu inutile, remplacé par un modal à une seule section (`{ section, value, forAllDevices }`).
- Testé dans le navigateur : changement du mode WEB via "Tous les appareils" → bascule sur "Tablette Eli" (qui n'a pas d'override) → elle hérite bien du nouveau mode, confirmant que la modification a bien touché le défaut de l'enfant et pas un appareil précis.

## "Ajouter un appareil" câblé avec code de pairing (2026-09-23)

- Nouveau composant `dashboard-ui/src/AddDeviceModal.jsx`, ouvert depuis "🖥️ Ajouter un appareil" dans `QuickActions`.
- Flux : saisie d'un nom d'appareil optionnel → `POST /api/children/:id/pairing-code` (déjà existant côté serveur) → affichage du **code à 6 chiffres** en gros, avec **compte à rebours** (15 min, tick chaque seconde) et bouton pour regénérer si expiré.
- **Sondage léger** (toutes les 3s, `fetchChildren` + comparaison du nombre d'appareils de l'enfant) pendant que le modal attend : dès qu'un appareil de plus apparaît pour cet enfant, le modal se ferme automatiquement et la liste se rafraîchit. Reste **provisoire** — pourrait être remplacé par une notification poussée au dashboard (ex. via un WebSocket dashboard↔serveur, sur le même principe que le canal appareil↔serveur) mais le sondage REST est suffisant pour l'instant et le dashboard n'a pas encore de canal temps réel équivalent à `/ws/device`.
- Le endpoint client `POST /api/pairing/complete` (déjà existant) n'a pas changé — c'est lui qu'un futur client Windows appellera avec le code + sa clé publique pour terminer l'appairage.
- **Testé en simulant un client** (aucun client réel n'existe encore) : génération du code dans l'UI → `curl -X POST /api/pairing/complete` avec ce code → l'appareil "Ordi bureau" apparaît dans le sélecteur en quelques secondes et le modal se ferme tout seul. Confirme que tout le flux serveur + UI fonctionne, prêt à recevoir un vrai client.

## Suppression d'appareil + confirmations (2026-09-23)

- **Suppression d'un appareil** : bouton icône seule 🗑️ (pas de texte, façon bouton ⚙ des cartes) à côté du sélecteur d'appareil (`DeleteDeviceButton` dans `App.jsx`), visible uniquement quand un vrai appareil est sélectionné (pas sur "Tous les appareils"). Confirmation inline (popover) avant suppression → `DELETE /api/devices/:id` (déjà existant côté serveur). Après suppression, retour automatique sur "Tous les appareils".
- **Confirmations pour toute suppression** (enfant et appareil) : les deux passent par une étape de confirmation explicite avant l'appel API — pour l'enfant, dans `ChildModal.jsx` (zone "danger" avec un premier clic qui affiche le message + bouton rouge "Confirmer la suppression", pas de suppression en un seul clic) ; pour l'appareil, même logique dans `DeleteDeviceButton`.
- **Bug de style corrigé au passage** : les classes CSS `danger-zone`/`danger-link`/`pill-btn.danger`/`danger-confirm` étaient utilisées dans `ChildModal.jsx` depuis leur création mais jamais définies dans `App.css` — la confirmation de suppression d'enfant s'affichait donc sans style. Ajouté et vérifié visuellement (bouton rouge, bonne mise en page).
- Testé dans le navigateur : suppression de "Tablette Eli" → disparaît du sélecteur, bascule sur "Tous les appareils" ; confirmation de suppression d'enfant vérifiée visuellement (annulée sans supprimer pour préserver les données de test).

## Client Windows — V1 (2026-09-23)

- Emplacement : `Nautilyou/client/` — **depuis le 2026-09-23, 3 projets .NET** (`Nautilyou.sln` à la racine) : `NautilyouShared` (modèles + protocole pipe), `NautilyouService` (enforcement, Worker Service), `NautilyouCompanion` (systray, WinForms). Voir section "Scission service/companion" plus bas pour le détail et le pourquoi. `dotnet build`/`dotnet run` depuis le dossier de chaque projet (ou `dotnet build` à la racine `client/` pour tout compiler d'un coup). `Microsoft.Extensions.Hosting.WindowsServices` référencé dans `NautilyouService` pour l'installation en vrai service Windows plus tard (`sc create`), mais **pas encore installé/testé comme service** — testé uniquement en mode console via `dotnet run`.
- **Choix d'architecture pragmatique pour cette V1** : synchronisation par **polling HTTP REST** toutes les 20s (`NautilyouApiClient.cs`) plutôt que par le DataChannel WebRTC P2P prévu dans le plan initial. Raison : valider toute la boucle métier (pairing → config → enforcement → activité) rapidement avant d'investir dans la complexité WebRTC/NAT traversal. Le signaling server (`server/src/signaling.js`) existe déjà côté serveur et sera branché dans une itération suivante — le P2P reste l'objectif, ce n'est pas un changement de plan, juste un séquençage pragmatique.
- **Pairing** (`Worker.EnsurePairedAsync`) : au premier lancement, prompt console pour l'URL serveur + le code à 6 chiffres → `POST /api/pairing/complete` → état sauvegardé dans `%ProgramData%\Nautilyou\pairing.json` (`PairingState.cs`). Aux lancements suivants, ce fichier est relu directement, pas de nouveau prompt.
  - **Sécurité simplifiée pour l'instant** : la "clé publique" envoyée au pairing est un GUID aléatoire, pas une vraie paire de clés asymétriques — noté en TODO dans le code (`Worker.cs`) et à reprendre avant tout déploiement réel (cf. section Sécurité du plan : trust-on-first-use façon WireGuard).
- **Suivi du temps d'écran** (`Worker.AccumulateUsedTime`) : compteur en mémoire incrémenté à chaque tick (15s) tant que l'utilisateur n'est pas inactif depuis plus de 2 min (détection via `GetLastInputInfo`, `IdleTime.cs`). Remise à zéro au changement de jour. Le compteur est envoyé au serveur à chaque tick — pas encore persisté localement (redémarrage du service = perte du compteur du jour, à corriger avant prod).
- **Enforcement** (`Worker.TickAsync`) : recalcule quota+plage horaire **localement** à chaque tick (même logique que `server/src/status.js`, dupliquée côté client pour réagir sans attendre le serveur) ; si non conforme → verrouillage immédiat de la session (`SessionLock.cs`, P/Invoke `LockWorkStation`).
- **Blocage de sites V1** (`HostsFileBlocker.cs`) : réécrit le fichier hosts Windows pour rediriger les domaines de la liste noire vers `127.0.0.1`. **Limitations connues** : nécessite les droits admin (échoue silencieusement avec juste un warning sinon — confirmé par le test, l'écriture a été refusée en mode utilisateur normal) ; ne gère que le mode liste noire, le mode liste blanche nécessiterait un vrai filtrage réseau (WFP ou proxy local), pas encore fait.
- **⚠️ Point d'attention pour les prochains tests** : le client applique l'enforcement réellement (verrouille la session, modifie le fichier hosts si lancé en admin) dès qu'il tourne. Toujours tester avec une config permissive (`quotaEnabled`/`windowEnabled` à `false`) ou prévenir l'utilisateur avant de relancer, sous peine de verrouiller sa session en plein test (déjà arrivé une fois lors du premier test HTTP polling, cf. historique de session).

### Pas encore fait côté client
- ~~Vraie paire de clés asymétriques pour le pairing~~ **fait le 2026-09-23** (RSA 2048 + DPAPI + défi/réponse, voir section dédiée plus bas).
- ~~Persistance locale du compteur de temps utilisé~~ **fait le 2026-09-23** (`LocalState.cs`, voir section dédiée plus bas).
- ~~Filtrage réseau réel pour le mode liste blanche~~ **fait le 2026-09-23** via un résolveur DNS local (`DnsFilterServer.cs`), voir section dédiée — logique validée en isolation, mais **le fonctionnement réel avec droits admin (port 53 + changement DNS système) n'a pas encore été testé**, à faire avant un vrai déploiement.
- ~~Limites par app~~ **fait le 2026-09-23** (`AppLimitEnforcer.cs`), voir section dédiée plus bas.
- ~~Icône systray~~ **fait le 2026-09-23**, et scindée dans un processus séparé (`NautilyouCompanion`) dès le départ — voir section dédiée plus bas.
- Installation réelle en tant que service Windows : **scripts prêts et testés** (`install/install-service.ps1`), mais **jamais exécutée pour de vrai** (nécessite l'admin, l'utilisateur s'en charge lui-même sur sa machine). Protection contre l'arrêt par un utilisateur non-admin : à vérifier une fois le service réellement installé (comportement par défaut de `New-Service`, pas testé).
- Démarrage automatique du Companion à l'ouverture de session : **scripts prêts** (`install/install-companion-autostart.ps1`), **pas encore exécutés** (modifient le dossier de démarrage réel de la session Windows, en attente de confirmation explicite de l'utilisateur).
- `PipeSecurity` explicite pour le pipe local service↔companion (nécessaire une fois le service en vrai SYSTEM, session différente du Companion).
- TLS/WSS pour la connexion WebSocket en production (actuellement `ws://` en clair, adapté au dev local uniquement).
- **Interface de pairing dans le Companion (remarque utilisateur du 2026-09-23), voir section dédiée juste en dessous** — le pairing se fait aujourd'hui via la console du Service (`dotnet run`), ce qui n'est pas utilisable par un parent normal. Doit passer par une UI dans le Companion.

## Décision P2P → WebSocket (2026-09-23)

Sur question de l'utilisateur ("le P2P est un choix... si tu as une solution plus simple je suis preneur"), le canal de communication client↔serveur a été **entièrement remplacé** : WebRTC DataChannel (P2P) abandonné au profit d'un **WebSocket persistant et authentifié**, directement entre le client et le serveur du parent.

**Raison** : le serveur doit de toute façon être joignable depuis internet pour le signaling WebRTC initial — donc le P2P n'évitait pas cette contrainte réseau. Et le "tiers à contourner" que le P2P sert normalement à éviter est ici... le propre serveur self-hosted du parent, pas un vrai tiers : bypasser son propre serveur n'apporte aucun bénéfice de confidentialité. Un WebSocket direct est bien plus simple (pas d'ICE/STUN/TURN/SDP, pas de lib WebRTC côté .NET type SIPSorcery) pour un résultat tout aussi réactif — et **bonus** : comme c'est le client qui initie la connexion sortante vers le serveur, l'enfant n'a besoin d'aucune configuration réseau de son côté (contrairement au P2P qui peut échouer sur NAT symétrique) ; seul le serveur du parent doit être joignable depuis internet.

**Implémenté et testé le 2026-09-23** :
- Serveur : `server/src/signaling.js` (relai SDP/ICE) remplacé par `server/src/deviceSocket.js` — WebSocket authentifié sur `/ws/device?deviceId=...&token=...` (le token = la clé de pairing du device). Expose `pushConfigToDevice()` et `pushCommand()`, appelés par les routes REST (`PUT /devices/:id/overrides`, `POST /devices/:id/bonus`, `POST /devices/:id/lock`, `PUT /children/:id/config`) pour pousser les changements **instantanément** si l'appareil est connecté. `deviceDetail()` extrait dans `server/src/deviceDetail.js` (partagé entre routes REST et WebSocket).
- Client : nouveau `DeviceSocketClient.cs` (WebSocket avec reconnexion automatique toutes les 5s si coupé). `Worker.cs` réécrit : reçoit la config en push (event `ConfigReceived`), reçoit les commandes instantanées (event `CommandReceived`, ex. verrouillage immédiat sans attendre le prochain tick), envoie l'activité via le socket (repli HTTP `POST /devices/:id/activity` si déconnecté).
- **Testé en conditions réelles avec un enfant de test à config permissive** (`quotaEnabled`/`windowEnabled` à `false`, pour éviter tout verrouillage accidentel) : pairing réel → connexion WebSocket établie → config reçue immédiatement à la connexion → **bonus temps accordé via l'API pendant que le client tournait → reçu en push côté client en moins de 3s, sans attendre de cycle** (confirmé dans les logs : `bonus 10` reflété dans le quota affiché) → déconnexion propre détectée côté serveur (`online` repasse à `false`).
- Un test antérieur (avant ce passage au WebSocket, sur la version HTTP polling) avait réellement verrouillé la session de l'utilisateur car la config de test était hors plage horaire — leçon retenue, tous les tests suivants utilisent une config de test permissive dédiée.

## Sécurité du pairing + persistance locale client (2026-09-23)

### Vraie paire de clés RSA (remplace le GUID de la V1)

- **Client** (`DeviceKeyStore.cs`) : génère une paire RSA 2048 bits au premier lancement, clé privée **chiffrée au repos via DPAPI** (`ProtectedData`, portée `LocalMachine` — cohérent avec un futur service tournant en SYSTEM) dans `%ProgramData%\Nautilyou\device.key`. La clé publique (format SPKI, base64) est envoyée au pairing à la place du GUID aléatoire de la V1.
- **Authentification WebSocket par défi/réponse** (pas un jeton statique) : à la connexion, le serveur génère un nonce aléatoire et l'envoie (`{type:"challenge", nonce}`) ; le client le signe avec sa clé privée (RSA-SHA256, PKCS1) et renvoie la signature (`{type:"auth", signature}`) ; le serveur vérifie avec la clé publique enregistrée au pairing (`server/src/deviceSocket.js`, timeout 5s). Aucun secret ne transite plus en clair sur le fil.
- **Validation serveur** : `POST /api/pairing/complete` rejette maintenant toute `publicKey` qui n'est pas une clé RSA SPKI valide (400 immédiat, avant même de consommer le code).
- `PairingState.cs` allégé (ne stocke plus de clé, juste `ServerUrl`/`DeviceId` — la clé vit dans `DeviceKeyStore`).

### Persistance locale client (`LocalState.cs`)

- Compteur de temps utilisé aujourd'hui **et** dernière config reçue du serveur sont maintenant sauvegardés dans `%ProgramData%\Nautilyou\local-state.json` à chaque tick / à chaque réception de config.
- Au démarrage : si la date sauvegardée correspond à aujourd'hui, le compteur de temps est restauré (sinon remis à zéro, changement de jour) ; la dernière config connue est rechargée immédiatement en mémoire, **avant même la connexion WebSocket**.
- Ça réalise enfin le comportement **fail-safe hors ligne** prévu dès le plan initial : la dernière config connue s'applique en continu, y compris juste après un redémarrage du service si le serveur n'est pas encore joignable — plus de fenêtre où le client ne fait rien faute de config.

### Testé en conditions réelles le 2026-09-23 (avec un enfant de test à config permissive, comme d'habitude pour ne rien verrouiller)
- Pairing avec la vraie clé RSA → défi/réponse réussi (logs : "Defi d'authentification signe et envoye" puis "Connecte et authentifie") → 3 fichiers créés (`device.key`, `pairing.json`, `local-state.json`).
- **Redémarrage du client** (simulant un redémarrage de service) → pas de nouveau prompt de pairing (clés + pairing réutilisés) → log "Derniere config connue rechargee depuis le cache local" → **premier cycle d'enforcement exécuté avec la config en cache avant même que le WebSocket ne soit reconnecté** (`ws connecte: False` mais décision correcte) → reconnexion WebSocket + ré-authentification RSA quelques secondes après. Confirme le fail-safe offline de bout en bout.

## Clarification : sémantique du mode liste blanche (confirmée le 2026-09-23)

Confirmé par l'utilisateur : en mode liste blanche, **tout site est inaccessible sauf ceux explicitement listés** (mode par défaut = bloqué, la liste blanche est la seule exception). Ça répond à la question ouverte posée plus tôt dans ce document.

**Statut d'implémentation** : le comportement est correct pour le mode **liste noire** (`HostsFileBlocker.cs` redirige les domaines listés vers `127.0.0.1`, tout le reste passe). Le mode **liste blanche** n'est **pas encore appliqué réellement** — un fichier hosts ne peut techniquement pas implémenter "tout bloquer sauf X" (il ne peut qu'ajouter des redirections ponctuelles, pas changer le comportement par défaut). Actuellement en mode liste blanche, `HostsFileBlocker` ne bloque simplement rien (cf. commentaire dans le code). Une vraie implémentation demandera soit un proxy/résolveur DNS local (répondre uniquement pour les domaines autorisés, NXDOMAIN sinon) soit un driver WFP — reste dans le "pas encore fait", maintenant avec la sémantique exacte à respecter clairement actée.

## Filtrage de sites par DNS local — liste noire ET liste blanche (2026-09-23)

Le hosts file ne peut structurellement pas implémenter "tout bloqué sauf X" (il ne fait qu'ajouter des redirections ponctuelles, il ne peut pas changer le comportement par défaut). Remplacé entièrement par un **petit résolveur DNS local**.

- **`DnsFilterServer.cs`** : écoute en UDP sur `127.0.0.1:53`. Pour chaque requête DNS reçue :
  - Parse le nom de domaine demandé (lecture directe des labels du paquet, pas de dépendance externe).
  - Décide autorisé/bloqué selon la config WEB courante (`IsAllowed()`) : liste noire → bloqué si dans `blacklist` (sous-domaines inclus) ; liste blanche → bloqué **sauf** si dans `whitelist`.
  - Si autorisé : relaie la requête telle quelle vers un DNS public en amont (1.1.1.1) et renvoie la réponse telle quelle (pass-through, pas de reconstruction de paquet).
  - Si bloqué : renvoie une réponse **NXDOMAIN** (bit QR + code retour modifiés directement sur les octets de la requête reçue, sans reconstruire tout le paquet — technique simple et robuste).
- **`DnsAdapterConfigurator.cs`** : pointe les interfaces réseau actives de la machine vers `127.0.0.1` via `netsh interface ip set dns ... static 127.0.0.1` (uniquement si `DnsFilterServer` a réussi à démarrer, donc uniquement si on a les droits requis) ; restaure le DNS automatique (`dhcp`) proprement à l'arrêt du service (`try/finally` autour de la boucle principale dans `Worker.cs`), pour ne jamais laisser la machine sans DNS fonctionnel si le client plante ou s'arrête.
- **`HostsFileBlocker.cs` supprimé** — devenu redondant, un seul mécanisme (DNS) gère maintenant les deux modes proprement.
- Câblé dans `Worker.cs` : `_dnsFilter.Start()` au démarrage (après avoir éventuellement rechargé la config en cache), `UpdateConfig()` appelé à chaque config reçue (push WebSocket) plutôt qu'à chaque tick — suffisant puisque la config web ne change que sur action du parent.

### Testé le 2026-09-23 — avec une réserve assumée sur la portée du test

- **Logique de filtrage validée en isolation**, via un harnais de test temporaire (`Program.cs`, retiré après usage) faisant tourner `DnsFilterServer` sur un **port non privilégié (25353)**, interrogé depuis Node.js (`dns.Resolver` pointé sur `127.0.0.1:25353`) :
  - Mode liste blanche (`wikipedia.org` autorisé) : `wikipedia.org` résout normalement (`185.15.58.224`), `example.com` renvoie `ENOTFOUND` (NXDOMAIN). ✅
  - Mode liste noire (`example.com` bloqué) : `example.com` renvoie `ENOTFOUND`, `wikipedia.org` résout normalement. ✅
  - Les deux modes confirmés par les logs serveur ("Site bloque (DNS) : example.com").
### Test en conditions réelles (port 53 + `netsh`) le 2026-09-23, avec incident mineur et correction

Contrairement à l'hypothèse initiale, **le bind sur `127.0.0.1:53` a réussi sans droits admin** sur cette machine (coexiste avec le service Windows "Client DNS" qui écoute sur `0.0.0.0:53` — deux adresses différentes sur le même port). `DnsAdapterConfigurator.PointAtLocalFilter()` (qui appelle `netsh`) a donc bien été exécuté en conditions réelles, pas juste en isolation.

- **Vérifié avant/après avec `Get-DnsClientServerAddress`** : le DNS de la machine n'a jamais changé (resté sur l'IP du routeur), et aucun process client ne restait actif après le test.
- **Bug trouvé et corrigé** : `netsh` refuse une commande faute de droits admin en renvoyant un **code de sortie non-nul**, pas en levant d'exception .NET — le code initial ne vérifiait que les exceptions (`try/catch` autour de `Process.Start`), donc croyait silencieusement que le DNS avait été reconfiguré alors que `netsh` avait juste échoué poliment. Corrigé : `DnsAdapterConfigurator.RunNetsh` capture maintenant stdout/stderr et vérifie `ExitCode`, `PointAtLocalFilter()`/`RestoreDhcp()` retournent un booléen de succès réel, et `Worker.cs` ne considère `_dnsAdapterConfigured` vrai que si au moins une interface a été **effectivement** reconfigurée (pas juste "le port 53 a pu être ouvert").
- Retesté après correction : logs maintenant explicites et exacts ("code 1 - L'opération demandée requiert une élévation (Exécuter en tant qu'administrateur)" pour chaque interface, puis résumé "le filtrage ne s'applique pas réellement").
- **Découverte annexe** : `NetworkInterface.GetAllNetworkInterfaces()` remonte beaucoup d'adaptateurs virtuels/pseudo (filtres WFP, QoS Packet Scheduler, WSL Hyper-V, VirtualBox...) en plus des vraies interfaces réseau — bruit dans les logs sans gravité (chaque tentative échoue proprement), mais pourrait être filtré plus finement plus tard (ex. ne garder que les interfaces avec une adresse IPv4 assignée) pour des logs plus lisibles.
- **Reste à faire** : tester avec de vrais droits admin (élévation UAC réelle, ou en tant que service Windows tournant en SYSTEM) pour valider que `netsh` réussit effectivement et que le filtrage s'applique pour de vrai. Pas fait dans cette session (pas de droits admin disponibles dans l'environnement de dev).

## Build Docker testé avec succès (2026-09-23)

Docker Desktop n'est pas installé nativement sur la machine de dev, mais **Docker est disponible dans WSL** (`wsl -e docker ...`) — utilisé pour tout ce test, pas besoin d'installer Docker Desktop côté Windows.

- `docker compose build` : succès, aucune compilation native (confirmé — l'usage de `node:sqlite` natif plutôt que `better-sqlite3` paie ici).
- `docker compose up -d` : le conteneur démarre et écoute sur `4100`, log de démarrage propre (`Nautilyou server (API + WebSocket appareils) sur http://localhost:4100`, avec le warning expérimental attendu de `node:sqlite`).
- **API accessible depuis Windows** via `http://localhost:4100` grâce au port-forwarding automatique WSL2 (léger délai après démarrage/redémarrage d'un process sur ce port, sans gravité).
- **Persistance testée** : création d'un enfant "DockerTest" → `docker compose restart` → l'enfant est toujours là (volume nommé `nautilyou_data` fonctionne).
- **WebSocket testé** : upgrade HTTP 101 réussi sur `/ws/device`, refus correct d'un `deviceId` inconnu ("appareil introuvable").
- Nettoyé après test : conteneur et volume de test supprimés, `version: "3.9"` retiré de `docker-compose.yml` (obsolète, généraient un warning), serveur de dev local relancé avec les données seed.

## Systray, page de blocage, demandes temps/accès (2026-09-23)

Grosse itération demandée par l'utilisateur : interface systray sobre côté client (temps restant + temps par app + bouton "demander plus de temps"), page de blocage soignée avec bouton "demander l'accès", et le circuit serveur pour approuver/refuser ces demandes depuis l'onglet Alertes.

### Serveur : système de demandes
- Nouvelle table `requests` (`device_id`, `type: 'time'|'site'`, `payload`, `status: 'pending'|'approved'|'denied'`).
- `server/src/requestsModel.js` (`createRequest`) partagé entre la route REST et le WebSocket pour éviter un import circulaire.
- `server/src/routes/requests.js` : `POST /api/requests` (créer, secours REST), `GET /api/requests?status=pending`, `POST /api/requests/:id/approve`, `POST /api/requests/:id/deny`.
  - Approbation **time** → ajoute un `bonus_grants` (réutilise la logique existante).
  - Approbation **site** → ajoute le domaine à la whitelist (si mode liste blanche) ou le retire de la blacklist (si mode liste noire), en **override appareil** (pas touché au défaut de l'enfant).
  - Dans les deux cas : push de la config à jour + commande `request-resolved` au client connecté.
- `deviceSocket.js` : nouveau type de message `request` reçu du client → `createRequest()`.
- Testé via curl bout en bout : création, approbation (bonus réellement appliqué, vérifié via `GET /api/devices/:id`).

### Dashboard : onglet Alertes fonctionnel
- Le bouton "Alertes" (avant, mort) ouvre maintenant `AlertsModal.jsx` : liste les demandes en attente (nom enfant + appareil + description lisible), boutons Approuver/Refuser.
- Badge du nombre de demandes en attente sur l'icône 🔔, sondage léger toutes les 5s (même pattern que le sondage de pairing).
- Testé dans le navigateur : demande créée via curl → apparaît avec badge → approuvée → badge repasse à 0, effet vérifié côté serveur (whitelist mise à jour).

### Client : systray sobre
- Projet basculé en `net10.0-windows` + `UseWindowsForms` (nécessaire pour `NotifyIcon`/`Form`).
- `TrayApp.cs` + `StatusForm` : icône générée en mémoire (pas de fichier .ico externe), tourne sur un **thread STA dédié** avec sa propre boucle de messages (`Application.Run`), séparé du `Worker` (thread pool). Fenêtre sobre : temps restant, liste du temps par app (triée), boutons +15/+30/+1h pour demander du temps.
- `ClientStatus.cs` (snapshot thread-safe lu par l'UI, écrit par le Worker) et `TrayBridge.cs` (événements bidirectionnels : clic bouton → Worker envoie la demande ; réponse serveur → balloon tip systray) font le pont entre les deux threads.
- **⚠️ Limite architecturale documentée** : un vrai service Windows (SYSTEM) tourne en **Session 0**, isolée de la session interactive — il ne peut **pas** afficher d'icône systray. Cette UI ne fonctionne que parce qu'on teste encore en mode console (session interactive). Une fois le vrai service installé, il faudra **scinder en deux** : le service (enforcement, pas d'UI) + une petite appli compagnon lancée dans la session utilisateur au démarrage, qui parle au service en local. Noté comme dette architecturale à ne pas oublier.

### Client : page de blocage (DNS sinkhole)
- `DnsFilterServer` ne renvoie plus NXDOMAIN pour un site bloqué mais un enregistrement A pointant vers un **sinkhole local (127.0.0.2)** — NXDOMAIN reste utilisé pour les types de requête autres que A.
- `BlockedPageServer.cs` : mini serveur HTTP (`HttpListener`) sur `127.0.0.2:80`, sert une page stylée (icône cadenas, nom du domaine, bouton "Demander l'accès" qui POST vers lui-même puis affiche une confirmation), branché sur l'envoi de la demande au serveur.
- **Limitation assumée et documentée** : ne fonctionne visuellement que pour le trafic **HTTP en clair**. La plupart des sites modernes forcent HTTPS ; le navigateur affichera son propre avertissement de sécurité (pas de certificat de confiance pour le domaine bloqué) avant que cette page ne puisse s'afficher. Pas d'installation de CA racine custom (option bien plus invasive, écartée pour l'instant).

### Bugs trouvés et corrigés pendant les tests
1. **Le compteur de temps ne s'incrémentait jamais.** Chaque cycle dure 15s (0,25min) ; `Math.Round(0.25min)` = 0 à chaque tick, donc `_usedMinutesToday` restait bloqué à 0 indéfiniment, peu importe la durée réelle passée. **Corrigé** : accumulation en **secondes** (`LocalState.UsedSecondsToday`, `AppSecondsToday`), les minutes ne sont calculées qu'au moment de les exposer (affichage, rapport au serveur, comparaison au quota) via `UsedMinutesToday => UsedSecondsToday / 60`. Vérifié : après 63s réelles, le log affiche correctement "Temps utilise: 1min".
2. **Détection d'activité par inactivité clavier/souris inadaptée** (remarque de l'utilisateur) : un enfant qui regarde une vidéo sans toucher ni souris ni clavier pendant plusieurs minutes se serait vu compter comme "absent", et son temps d'écran n'aurait pas compté. **Remplacé** par la détection de **verrouillage de session** (`SessionLockState.cs`, abonnement à `Microsoft.Win32.SystemEvents.SessionSwitch`, câblé depuis le thread STA du systray qui est le seul endroit du client à avoir une boucle de messages Windows active). Actif = session déverrouillée, peu importe l'activité clavier/souris. `IdleTime.cs` supprimé (devenu inutile).
3. Testé : après correction, le temps s'accumule bien **sans aucune activité souris/clavier simulée** (session restée déverrouillée), confirmant que le nouveau signal fonctionne comme voulu.

### Ce qui a été testé vs pas testé dans cette itération
- **Testé en conditions réelles** : pairing, config permissive, accumulation du temps (bug + correctif confirmés), suivi par app (process `claude` correctement identifié comme app au premier plan), demande de temps/accès (flux serveur complet via curl + dashboard), page de blocage (rendu + bouton, testée sur port non privilégié via navigateur).
- **Pas testé** : rendu visuel réel de l'icône systray et de la fenêtre `StatusForm` (impossible de "voir" l'écran Windows depuis les outils disponibles ici — juste confirmé que le thread STA démarre sans exception) ; clic réel sur les boutons de la fenêtre systray (même limite) ; page de blocage sur le vrai port 80 + sinkhole réel (nécessite les droits admin, échoue proprement comme les autres fonctionnalités admin-only) ; comportement HTTPS de la page de blocage (nécessiterait un certificat + droits admin).

## Scission service/companion + limites par app (2026-09-23)

### Architecture scindée en 3 projets .NET
Le client était un seul processus (Worker Service + WinForms) — problème : un vrai service Windows tourne en **Session 0**, isolée de la session interactive, et ne peut **pas** afficher d'icône systray. Scindé en :

- **`NautilyouShared`** (class library, `net10.0`) : modèles partagés (`DeviceConfig`, `WebConfig`, etc., déplacés depuis l'ancien `DeviceModels.cs`) + protocole du pipe (`PipeProtocol.cs`, `PipeMessenger.cs` — JSON ligne par ligne, même forme `{type, payload}` que le WebSocket).
- **`NautilyouService`** (Worker Service, `net10.0`, **sans WinForms**) : tout l'enforcement — pairing, WebSocket serveur, filtre DNS, page de blocage, verrouillage, **limites par app** (nouveau). Reçoit `AppLimitEnforcer.cs`, `PipeServer.cs` (nouveau, héberge le pipe local).
- **`NautilyouCompanion`** (WinForms, `net10.0-windows`) : uniquement le systray + `PipeClient.cs` (nouveau, se connecte au pipe du service, reconnexion auto toutes les 5s) + `SessionLockState.cs` (déplacé ici — c'est le seul endroit qui a une boucle de messages Windows pour recevoir `SystemEvents.SessionSwitch`).
- `Nautilyou.sln` créé à la racine de `client/` pour builder les 3 en même temps.
- `TrayBridge.cs` (in-process) supprimé, remplacé par le pipe (cross-process) : `PipeServer`/`PipeClient` avec les mêmes 4 types de messages (`status`, `balloon` du service vers le companion ; `time-request`, `session-lock` du companion vers le service).

**⚠️ Sécurité du pipe non finalisée** : ACL par défaut, fonctionne tant que service et companion tournent sous le même compte (mode dev actuel). Une fois le service installé en vrai service SYSTEM, il faudra une `PipeSecurity` explicite autorisant le SID de l'utilisateur interactif — noté comme TODO, pas fait.

**Comportement si le Companion n'est pas connecté** : le service suppose la session **active** par défaut (`_companionReportsLocked = false` tant qu'aucun message contraire n'est reçu) — choix délibéré pour qu'un enfant qui tuerait l'appli compagnon ne récupère pas du temps gratuit illimité ; le suivi du temps continue même sans companion, juste sans le signal fin de verrouillage.

### Limites par app (`AppLimitEnforcer.cs`, nouveau)
- Appelé à chaque tick avec `AppsConfig` (déjà reçu du serveur) + le suivi par app déjà en place (`AppSecondsToday`).
- Pour chaque règle de limite (`AppLimit`), si le temps cumulé sur les apps de la règle dépasse `MinutesPerDay` → **termine le process** (`Process.Kill()`) de chaque app concernée actuellement en cours.
- Les `BlockedApps` (blocage total, indépendant du temps) sont fermées immédiatement si détectées en cours d'exécution.
- **Correspondance app ↔ process** approximative (égalité ou inclusion insensible à la casse) — un parent qui tape "chrome" bloquera le process "chrome" ; un parent qui tape "Instagram" ne bloquera rien de spécifique sur desktop (pas de process natif "Instagram.exe" — limitation de fond déjà connue, non résolue ici).
- **Enforcement volontairement brutal** : `Process.Kill()` direct, pas de fermeture "douce" (WM_CLOSE) — l'enfant perd son travail non sauvegardé dans cette app. Compromis assumé pour la simplicité/fiabilité de cette V1.

### Bug trouvé et corrigé pendant les tests : désérialisation JSON du pipe
Les messages `time-request`/`session-lock`/`status`/`balloon` étaient désérialisés sans `JsonSerializerOptions` → `System.Text.Json` reste sensible à la casse par défaut, donc le JSON envoyé en camelCase (`"minutes"`) ne correspondait pas à la propriété C# PascalCase (`Minutes`) et retombait silencieusement sur sa valeur par défaut (0). **Une demande de "+25min" arrivait donc à "+0min" côté serveur.** Corrigé en exposant `PipeMessenger.JsonOptions` (Web defaults) et en l'utilisant pour toutes les désérialisations côté service et companion.

### Testé en conditions réelles le 2026-09-23
- **Build complet des 3 projets** (`dotnet build` sur la solution) : succès.
- **Service + Companion lancés séparément, connexion pipe confirmée dans les deux logs** ("Appli compagnon connectee au pipe local" côté service, "Connecte au service Nautilyou" côté companion).
- **Suivi du temps + par app** : toujours fonctionnel après la scission (vérifié via `local-state.json`).
- **Limites par app** : Notepad lancé manuellement, limite de 0min/jour poussée depuis le dashboard → log "Fermeture de Notepad (limite de 0min/jour depassee)" → process bien terminé (vérifié absent de `Get-Process`).
- **Demande de temps via le pipe** : simulée directement via PowerShell (`NamedPipeClientStream`) pour ne pas dépendre d'un clic UI que je ne peux pas déclencher depuis mes outils → bug de désérialisation trouvé (0min au lieu de 25min) → corrigé → retesté avec succès (25min bien reçues côté serveur).
- **Non testé** : clic réel sur les boutons de la fenêtre systray (toujours impossible de "voir"/interagir avec l'écran Windows depuis les outils disponibles ici) ; démarrage automatique du Companion à l'ouverture de session (pas encore implémenté, voir "pas encore fait") ; PipeSecurity multi-utilisateur (nécessite un vrai service SYSTEM + session utilisateur différente pour être pertinent).

## Installation réelle du service + auto-démarrage du Companion (2026-09-23)

Scripts PowerShell dans `Nautilyou/client/install/` :

- **`install-service.ps1`** (admin requis) : publie `NautilyouService` en autonome (self-contained, single-file, `win-x64` — pas besoin du runtime .NET sur la machine cible), copie vers `C:\Program Files\Nautilyou\Service`, enregistre via `New-Service` (démarrage automatique) + `sc.exe failure` pour un redémarrage auto en cas de crash, démarre le service. Vérifie les droits admin en premier et s'arrête proprement sinon (`IsInRole(Administrator)`).
- **`uninstall-service.ps1`** (admin requis) : arrête, supprime le service et ses binaires. Ne touche pas à `%ProgramData%\Nautilyou` (pairing/état local) — à supprimer manuellement si besoin.
- **`install-companion-autostart.ps1`** (pas d'admin nécessaire) : publie `NautilyouCompanion` en autonome, copie vers `%LocalAppData%\Nautilyou\Companion`, crée un raccourci dans le dossier Démarrage de l'utilisateur courant (`shell:startup`) pour un lancement automatique à l'ouverture de session.
- **`uninstall-companion-autostart.ps1`** : retire le raccourci + les binaires, tue le process s'il tourne.

### Bug corrigé pendant les tests : pairing interactif impossible sur un vrai service
`EnsurePairedAsync` (`Worker.cs`) utilisait `Console.ReadLine()` pour le pairing — un vrai service Windows tourne en **Session 0 sans console interactive**, donc ce code aurait renvoyé des chaînes vides et échoué en boucle silencieusement au premier démarrage sans pairing préalable. **Corrigé** : détection via `Environment.UserInteractive` — si false et aucun pairing existant, le service log une erreur claire ("Appaire d'abord via 'dotnet run' en mode console...") et attend en boucle (retente toutes les 30s) plutôt que de planter. Le flux normal reste : **toujours appairer une fois via `dotnet run` en console AVANT d'installer le service** — `pairing.json`/`device.key` existent alors déjà et ce chemin n'est jamais emprunté.

### Bug corrigé pendant les tests : encodage des scripts PowerShell
Les caractères non-ASCII (tiret cadratin "—", emoji "⚠️") dans les chaînes `Write-Host` en dehors des blocs de commentaire `<# #>` faisaient planter le parseur PowerShell 5.1 avec une erreur "terminateur manquant" — PowerShell 5.1 lit les `.ps1` sans BOM avec l'encodage ANSI du système par défaut, pas UTF-8, donc les caractères multi-octets se décodaient en bytes parasites qui cassaient le parsing des chaînes. **Corrigé** : tous les caractères non-ASCII remplacés par leurs équivalents ASCII (tiret simple, "ATTENTION :") dans le code exécuté ; ceux qui restent dans les blocs de commentaire ne posent pas de problème (le tokenizer ne les interprète pas).

### Testé le 2026-09-23
- `install-service.ps1` exécuté sans droits admin → détection propre, message d'erreur clair, arrêt net (confirme que le garde-fou fonctionne).

### Installation réelle effectuée par l'utilisateur — incident DNS réel et corrections (2026-09-23)

L'utilisateur a installé le service pour de vrai, en admin, sur sa machine de tous les jours (pas un environnement jetable). Déroulé complet :

1. **`install-service.ps1` exécuté avec succès** en PowerShell admin — service installé et démarré.
2. **Premier blocage** : le service tournait mais restait bloqué en boucle ("Aucun pairing existant...") — normal, pas encore appairé. Diagnostiqué via `Get-EventLog -LogName Application -Source NautilyouService`, qui a confirmé que le log applicatif du service fonctionne comme prévu.
3. **Deuxième bug trouvé** : le pairing en console (`dotnet run`, pour appairer avant l'install du service) plantait tout le process avec `UriFormatException` quand l'utilisateur tapait `localhost:4100` sans le préfixe `http://`. **Corrigé** : `EnsurePairedAsync` (`Worker.cs`) préfixe automatiquement `http://` si absent, valide l'URL avec `Uri.TryCreate` avant d'essayer, et boucle sur une nouvelle saisie au lieu de laisser planter le `BackgroundService` (donc tout le host) sur une exception de pairing. Testé et confirmé : `localhost:4100` fonctionne maintenant.
4. **Pairing réel réussi** : `dotnet run` en console a réussi à s'appairer, et pour la première fois, **`netsh` a réellement réussi à repointer le DNS système** vers `127.0.0.1` sur plusieurs interfaces réelles (jamais obtenu dans mon environnement sandboxé, qui n'a pas les droits admin). Confirmé côté serveur : l'appareil "FIXE-JIMMY" apparaissait online en temps réel.
5. **⚠️ Incident réel : plus aucun site ne chargeait.** Cause identifiée : l'utilisateur a fait `Restart-Service` sans avoir fermé (`Ctrl+C`) la session console `dotnet run` encore ouverte dans une autre fenêtre. Le service a échoué à prendre le port 53 ("déjà utilisé" — confirmé dans le journal d'événements), donc le DNS système était toujours pointé vers **la session console**, pas vers le service. Quand cette session console a fini par se terminer sans que son nettoyage (`RestoreDhcp()`) s'exécute complètement, le DNS est resté bloqué sur `127.0.0.1` sans plus rien qui écoute derrière → panne totale de résolution DNS sur la machine réelle de l'utilisateur.
6. **Résolu immédiatement** par l'utilisateur avec la commande de secours donnée à l'avance :
   ```powershell
   Get-NetAdapter | Where-Object Status -eq "Up" | ForEach-Object { netsh interface ip set dns name="$($_.Name)" dhcp }
   ipconfig /flushdns
   ```
7. **Deux mesures de sécurité ajoutées après l'incident** :
   - **`install/restore-dns.ps1`** : script "bouton panique" dédié, fait exactement la commande de secours ci-dessus en un seul script facile à retrouver (pas besoin de se souvenir de la commande).
   - **Filet de sécurité dans le code** (`Program.cs` du service) : un handler `AppDomain.CurrentDomain.ProcessExit` retente `DnsAdapterConfigurator.RestoreDhcp()` en dernier recours si le `finally` normal de `Worker.ExecuteAsync` ne s'est pas exécuté jusqu'au bout. N'aide pas contre un kill dur (fin de tâche forcée, coupure), mais couvre plus de cas de sortie "moins propres" qu'avant.

**Leçon retenue** : le changement de DNS système est une action réelle et risquée dès qu'on a les droits admin — largement anticipé en théorie (avertissements donnés avant chaque test), mais l'incident a quand même eu lieu à cause d'un **conflit entre deux instances** (console de pairing encore ouverte + service fraîchement démarré) plutôt qu'un bug du filtre DNS lui-même. À l'avenir : toujours s'assurer que la session console de pairing est bien fermée (`Ctrl+C`, confirmer le retour au prompt) avant de démarrer/redémarrer le service.

**Recommandation en attendant plus de robustesse** : passer `NautilyouService` en démarrage **Manual** plutôt qu'Automatic le temps de fiabiliser davantage (`Set-Service NautilyouService -StartupType Manual`, en admin), pour éviter un redémarrage DNS surprise après un reboot Windows.

- **Auto-démarrage du Companion installé le 2026-09-23** (confirmation explicite obtenue) : `install-companion-autostart.ps1` exécuté avec succès — publié en self-contained (`win-x64`, single-file), copié dans `%LocalAppData%\Nautilyou\Companion`, raccourci créé dans `%AppData%\...\Startup\Nautilyou.lnk`. Le Companion se lancera désormais automatiquement à chaque ouverture de session Windows. `uninstall-companion-autostart.ps1` disponible pour revenir en arrière si besoin.

## Interface de pairing dans le Companion (à faire — remarque utilisateur du 2026-09-23)

**Constat** : le pairing se fait aujourd'hui exclusivement via la console du Service (`dotnet run`, prompts `Console.ReadLine()`). Ce n'est pas praticable pour un vrai parent utilisateur final — personne ne va ouvrir un terminal et lancer une commande .NET. L'incident DNS ci-dessus est d'ailleurs directement lié à cette UX bancale (une session console de pairing qu'on oublie de fermer).

**Design cible proposé** :
- Au lancement, le **Companion** vérifie s'il existe un pairing (en interrogeant le Service via le pipe, ou en se basant sur le premier message `status` reçu — actuellement le Service ne pousse un `status` qu'une fois `_latestDevice` non nul, donc l'absence de message après un délai peut déjà servir de signal "pas encore appairé").
- **Tant que non appairé** : le Companion affiche un formulaire de pairing (URL du serveur + code à 6 chiffres) **à la place** de l'écran de statut habituel — pas de temps restant affiché, pas de liste d'apps, rien tant que le pairing n'a pas réussi.
- Le Companion envoie `{serverUrl, code}` au Service via le pipe (nouveau type de message, ex. `"pair-request"`).
- Le **Service** (qui détient les clés RSA via `DeviceKeyStore`) effectue l'appel réel à `POST /api/pairing/complete`, sauvegarde `PairingState`, puis répond au Companion via le pipe (`"pair-result"`, succès/échec + message d'erreur le cas échéant) pour que le formulaire affiche un retour clair.
- Une fois le pairing confirmé, le Companion bascule automatiquement sur l'écran de statut normal (le prochain message `status` du Service déclenche la transition).
- La console du Service (`dotnet run`) resterait un **mode de secours pour le dev/debug uniquement**, plus le chemin principal pour un vrai déploiement.

**Réponse à la question "comment sait-il quel serveur joindre ?"** : oui, il faut taper l'URL **et** le code — c'est inhérent au self-hosted (pas d'adresse fixe connue à l'avance comme pour un service cloud, chaque famille héberge son propre serveur). Amélioration possible plus tard pour réduire la friction : le dashboard pourrait afficher une **chaîne de pairing combinée** (URL + code concaténés, à copier-coller en un seul champ dans le Companion) plutôt que deux saisies séparées — pas fait, juste une piste notée.

**Implémenté le 2026-09-23** :
- `NautilyouShared/PipeProtocol.cs` : `PairingNeededMessage`, `PairRequestMessage {ServerUrl, Code}`, `PairResultMessage {Success, Message}`.
- `NautilyouService/PairingService.cs` (nouveau) : `TryPairAsync(serverUrl, code)` — logique partagée entre le prompt console (dev) et le pairing par pipe, extraite pour éviter la duplication.
- `NautilyouService/PipeServer.cs` : gère le message `"pair-request"` reçu du Companion (appelle `PairingService.TryPairAsync`, répond `"pair-result"` sur la même connexion) et expose `BroadcastPairingNeededAsync()`.
- `NautilyouService/Worker.cs` : `EnsurePairedAsync` diffuse `pairing-needed` par le pipe toutes les 5s tant que non appairé (le pipe est démarré avant l'appairage, contrairement à avant).
- `NautilyouCompanion/PipeClient.cs` : events `PairingNeeded`/`PairResultReceived`, méthode `SendPairRequestAsync(serverUrl, code)`.
- `NautilyouCompanion/TrayApp.cs` (`StatusForm`) : 3 vues dans la même fenêtre — "chargement" (au tout premier lancement, avant tout message du Service), "pairing" (formulaire URL + code + bouton "Appairer", affiché dès réception de `pairing-needed`), "statut" (écran habituel, affiché dès réception du premier `status` — implique un pairing réussi). Transition automatique pairing → statut au premier `status` reçu.
- Build de la solution (`dotnet build Nautilyou.slnx`) : **0 erreur**.
- **Testé en conditions réelles avec succès le 2026-09-23** : Service + Companion lancés séparément, formulaire de pairing bien apparu dans le Companion, appairage réussi via le formulaire ("Appairage reussi : appareil 'FIXE-JIMMY'."), grâce au script `quick-pairing.ps1` (voir plus bas) pour générer le code sans friction. Le bug de diffusion unique de `pairing-needed` (voir plus bas) a été corrigé avant ce test réussi.

**Second bug corrigé le 2026-09-23 (plus important) : le Worker ne basculait jamais vers l'écran de statut malgré un pairing réussi.** `EnsurePairedAsync` (branche interactive/console) restait bloqué sur `Console.ReadLine()` en attendant une saisie clavier dans la fenêtre du Service, même après qu'un pairing ait réussi entre-temps via le pipe (Companion). Tant que cette méthode ne retournait pas, le reste du `Worker` (DNS, socket, boucle `TickAsync` qui diffuse les `status`) ne démarrait jamais, donc le Companion restait bloqué indéfiniment sur "Connexion...". Corrigé en vérifiant `PairingState.Load()` à chaque itération de `ReadLineWithPairingBroadcastAsync` (retourne `null` si un pairing a réussi entre-temps, ce qui fait abandonner la saisie console en cours) et au début de chaque tour de la boucle principale. **Confirmé fonctionnel après correction** : la fenêtre Companion a fini par basculer sur l'écran de statut. Latence perçue par l'utilisateur ("c'est long") : due au pipe qui retentait une connexion toutes les 5s — **resserré le 2026-09-23** à 1.5s côté `PipeClient.RunAsync` (Companion) et à 2s pour la diffusion `pairing-needed` côté `Worker.EnsurePairedAsync`/`ReadLineWithPairingBroadcastAsync` (Service). Compile (0 erreur), pas encore retesté en conditions réelles après ce resserrage.

**Incident DNS #2 le 2026-09-23** : après un test de pairing, l'utilisateur a de nouveau perdu l'accès internet (DNS repointé vers le filtre local du Service, comme le premier incident). Résolu immédiatement avec `restore-dns.ps1`. Service arrêté ensuite par précaution.

**Garde-fou ajouté le 2026-09-23 suite aux 2 incidents : `NautilyouService/DnsWatchdog.cs` (nouveau).** Après que `DnsAdapterConfigurator.PointAtLocalFilter` ait réussi, le Worker démarre un watchdog qui envoie une vraie requête DNS (type A) à `127.0.0.1:53` toutes les 5s — exactement le chemin qu'emprunterait un vrai navigateur — et vérifie qu'une réponse valide revient (peu importe le contenu : acceptée, bloquée, NXDOMAIN — seul compte que quelque chose répond). Après 3 échecs consécutifs (~15s d'indisponibilité), il restaure automatiquement le DNS DHCP (`DnsAdapterConfigurator.RestoreDhcp`) sans attendre d'intervention manuelle, et se met en pause tant que `_dnsAdapterConfigured` reste false. Branché dans `Worker.ExecuteAsync` juste après la reconfiguration réussie du DNS. Compile (0 erreur).

**Validé en isolation le 2026-09-23** (script de test jetable, port de test 15353, aucune modification du DNS système réel) : 3 scénarios passés — résolveur qui répond → silencieux ; résolveur mort → restauration déclenchée après 15,1s (3 échecs à 5s d'intervalle) ; `isAdapterConfigured=false` → pas de fausse alerte. Logique confirmée correcte. **Pas encore testé en conditions réelles complètes** (vrai port 53 + vrai `netsh`) mais confiance élevée vu la validation isolée.

**Cause racine du 1er incident identifiée avec précision** : ce n'était pas un service tiers sur le port 53, mais un conflit entre deux instances de Nautilyou lui-même (une session console de pairing restée ouverte + le vrai service redémarré par-dessus). **Corrigé le 2026-09-23** : `Program.cs` (NautilyouService) prend désormais un verrou mono-instance (`Mutex` nommé `Global\NautilyouServiceSingleInstance`) — une deuxième instance refuse de démarrer au lieu de rentrer en conflit sur le port DNS.

**Clarification apportée à l'utilisateur** : le filtre en liste noire (mode par défaut) avec une liste vide n'a jamais pu être la cause des pannes — liste noire vide = rien n'est bloqué, tout est transmis normalement. Seul le mode liste blanche avec une liste vide bloquerait tout ; ce n'est pas le mode par défaut (`defaultConfigTemplate()` dans `server/src/config.js` : `whitelistMode: false`).

**Incident DNS #3 (mais différent des 2 premiers) le 2026-09-23** : après ajout de "facebook.com" à la liste noire de "Papa" (via le dashboard) et relance du Service en mode normal, le blocage a fonctionné quelques secondes (page "site bloqué" affichée) puis Facebook a recommencé à fonctionner tout seul. **Diagnostic via les logs** : le filtre résolvait correctement (`aistudio.google.com -> autorise`, forward OK) juste avant, puis le watchdog a détecté 3 échecs consécutifs de son propre healthcheck (`nautilyou-healthcheck.local` vers `127.0.0.1:53`) sur ~15s et a déclenché la restauration DHCP automatiquement — **première fois que l'auto-guérison fonctionne sans intervention manuelle** (pas besoin de relancer `restore-dns.ps1`). Cause exacte du hoquet du filtre encore inconnue (le catch de `DnsWatchdog.CheckAsync` avalait l'exception sans la logger).

**Outils de diagnostic ajoutés le 2026-09-23** (pour la prochaine fois que ça arrive) :
- `DnsWatchdog.CheckAsync` logue maintenant l'exception complète en cas d'échec (type + message), au lieu de l'avaler silencieusement.
- `DnsFilterServer` : logue chaque requête DNS reçue (`"Requete DNS : {Domain} -> {Decision}"`) et chaque forward réussi (`"Resolution en amont OK"`), en `LogInformation` (visible par défaut).
- Variable d'environnement `NAUTILYOU_SKIP_DNS_ADAPTER=1` (nouveau, dans `Worker.ExecuteAsync`) : laisse le filtre tourner sur le port 53 sans jamais toucher au DNS système réel — permet de diagnostiquer via `nslookup domaine 127.0.0.1` sans aucun risque. **Utilisé avec succès** pour confirmer que le filtre lui-même (blocage ET forward) fonctionne parfaitement en isolation (`facebook.com` -> `127.0.0.2`, `youtube.com` -> vraie IP).

**Bug corrigé le 2026-09-23 : `DnsAdapterConfigurator.ActiveAdapterNames()` ciblait des pseudo-interfaces.** Reconfigurait le DNS sur TOUTES les interfaces "Up" retournées par `NetworkInterface.GetAllNetworkInterfaces()`, y compris des pseudo-interfaces internes Windows sans rapport avec la connexion réelle (`WFP Native MAC Layer Filter-0000`, `QoS Packet Scheduler-0000`, `VirtualBox NDIS Light-Weight Filter-0000`, visibles dans les logs — une quinzaine d'entrées à chaque démarrage). Corrigé pour ne cibler que les interfaces ayant une vraie passerelle IPv4 (`GetIPProperties().GatewayAddresses`), ce qui exclut naturellement ces pseudo-adaptateurs. Vérifié sur la machine de l'utilisateur (`Get-NetIPConfiguration` en lecture seule) : une seule vraie interface concernée (`Ethernet`).

**Durcissement le 2026-09-23 suite au diagnostic** : `DnsFilterServer.Start` agrandit le buffer reseau du socket d'ecoute a 1 Mo. `DnsWatchdog` assoupli en parallele : `CheckTimeout` 2s->3s, `FailureThreshold` 3->4 (~20s avant restauration DHCP au lieu de ~15s).

**2e test avec ces renforcements : meme symptome, mais logs bien plus clairs cette fois** (`"healthcheck en timeout apres 00:00:03"` x4, echec total et consecutif sur toute la fenetre de ~20s, pas juste un echec isole). Ca a permis de trouver la vraie cause.

**VRAIE CAUSE TROUVEE ET CORRIGEE le 2026-09-23 : bug classique de socket UDP sous Windows dans `DnsFilterServer.ListenLoopAsync`.** La boucle d'ecoute avait un `catch { break; }` generique autour de `_listener.ReceiveAsync(ct)`. Or sous Windows, un ICMP "port injoignable" recu en reponse a une requete forwardee vers un site qui ne repond pas (tres frequent, pas une vraie panne) peut faire lever une `SocketException` (WSAECONNRESET) au **prochain** `ReceiveAsync`, meme si le socket est parfaitement valide. Ce `catch` generique tuait alors **toute la boucle d'ecoute silencieusement et definitivement** (aucun log), sans jamais la redemarrer : le filtre restait ensuite completement muet (plus aucune requete traitee, meme le healthcheck du watchdog) jusqu'a la restauration DHCP automatique. Explique parfaitement le pattern observe : ca marchait, un paquet "poison" (ICMP) tuait la boucle, plus rien ne repondait jamais plus.

**Corrections** (`DnsFilterServer.cs`) :
- `ListenLoopAsync` ne `break` plus que sur `OperationCanceledException`/`ObjectDisposedException` (vraie fermeture) ; toute autre exception (dont la `SocketException` ICMP) est logguee et la boucle **continue** d'ecouter.
- `SIO_UDP_CONNRESET` desactive explicitement sur le socket au demarrage (Windows uniquement) pour empecher l'exception de se produire du tout, en complement du fix ci-dessus.

Compile (0 erreur). **Validé en conditions réelles le 2026-09-23** : `facebook.com` reste bloqué durablement (page "site bloqué" affichée, navigation prolongée, pas de nouveau "Watchdog DNS"), le vrai bug est corrigé. Confirmation utilisateur : "ça fonctionne !!!!".

**DNS-over-HTTPS (DoH) des navigateurs : traité partiellement le 2026-09-23.** Chrome/Firefox peuvent résoudre via un résolveur chiffré externe et contourner le filtre local. `DnsFilterServer` répond désormais NXDOMAIN (quand la supervision web est active) aux noms des principaux résolveurs DoH (Google, Cloudflare, Quad9, OpenDNS, AdGuard, NextDNS, Mullvad, etc.) et au domaine canari de Firefox `use-application-dns.net` : Chrome/Edge en mode automatique retombent sur le DNS système, Firefox désactive son DoH. Testé en isolation (port de test) : NXDOMAIN pour ces noms, `example.com` résolu normalement, et plus rien de neutralisé quand la supervision est coupée. **Non couvert** : navigateur configuré manuellement avec un résolveur DoH absent de la liste ou joint directement par adresse IP. Pour un durcissement complet, l'option serait une politique navigateur (clé de registre `DnsOverHttpsMode=off` pour Chrome/Edge) — modifie les réglages système, à ne faire que sur confirmation explicite ; pas fait.

**Bilan de fin de journée sur le filtrage DNS : résolu.** Le filtre bloque et transmet correctement, de façon stable dans la durée (validé en conditions réelles, navigation prolongée sur un site bloqué sans coupure). Le bug racine du "hoquet" (SocketException UDP tuant la boucle d'écoute silencieusement) est identifié et corrigé. Le verrou mono-instance, le watchdog et `restore-dns.ps1` restent en place comme filets de sécurité en profondeur pour d'autres causes de panne (process tué, conflit de port, etc.), même si le bug spécifique du jour est réglé.

## "Sites les plus consultés" à la place de "Catégories les plus actives" (2026-09-23)

**Constat utilisateur** : la carte WEB du dashboard affichait "Catégories les plus actives" mais toujours "Aucune donnée" — en creusant, ce champ (`topCategories`) n'a jamais été réellement alimenté nulle part (ni catégorisation de site implémentée côté client, ni donnée envoyée). C'était un placeholder mort depuis la création du dashboard. Remplacé par la vraie liste des domaines effectivement visités, dérivée du filtre DNS qui voit déjà passer chaque requête.

**Implémentation** :
- **Client** (`DnsFilterServer.cs`) : `ConcurrentDictionary<string,int> _siteCounts` incrémenté à chaque requête DNS **autorisée** (pas les sites bloqués), sur le domaine racine approximatif (2 derniers labels, ex. `www.youtube.com` → `youtube.com` — limitation connue : ne gère pas les suffixes composés type `.co.uk`). Exclut les requêtes techniques (`*.arpa`, notre propre domaine de healthcheck du watchdog). `GetTopSites(n)` / `ResetDailyCounters()` exposés ; le reset est appelé au même endroit que le reset quotidien de `UsedSecondsToday` dans `Worker.AccumulateUsedTime`.
- **Client → serveur** : `Worker.TickAsync` récupère `_dnsFilter.GetTopSites(8)` et le passe à `DeviceSocketClient.SendActivityAsync` (WebSocket) et `NautilyouApiClient.ReportActivityAsync` (repli HTTP), tous deux mis à jour pour accepter `topSites`.
- **Serveur** : colonne `activity_snapshots.top_categories` renommée `top_sites` (migration `ALTER TABLE ... ADD COLUMN` ajoutée dans `db.js` car `CREATE TABLE IF NOT EXISTS` ne touche pas une table déjà existante — la base de dev de l'utilisateur avait déjà l'ancien schéma). `deviceSocket.js`, `routes/devices.js`, `deviceDetail.js`, `seed.js` mis à jour en conséquence (le seed utilise maintenant de faux domaines réalistes à la place de fausses catégories).
- **Dashboard** (`App.jsx`) : `WebCard` affiche maintenant "Sites les plus consultés" à partir de `activity.topSites` (même rendu en tags qu'avant).

**Retour du 1er test réel (2026-09-23)** : la liste se remplit, mais elle comptait des requêtes DNS : `googleapis.com 44`, `gstatic.com`, `fbcdn.net`, `wpad.localdomain` dominaient, bien au-delà de ce que l'enfant voit à l'écran. Corrigé : (1) liste d'exclusion de domaines d'infrastructure (CDN, API, télémétrie, mises à jour, noms locaux) dans `ShouldCountAsVisit` ; (2) un site compte au plus **une fois par tranche de 5 min** (`CountVisit`), donc la valeur est un temps de présence approximatif ; (3) le dashboard affiche `~{count*5} min` avec une infobulle. Limite : un onglet ouvert en arrière-plan (mail, messagerie) compte comme de la présence. Compile (0 erreur), à retester. À noter : le titre "Résumé d'activité (7 derniers jours)" du dashboard ne correspond pas aux données (activité du jour seulement).

Vérifié : build client 0 erreur, syntaxe serveur valide (`node --check` sur tous les fichiers touchés), dashboard-ui (serveur de dev Vite déjà lancé) sans erreur de compilation. **Pas encore testé de bout en bout avec un vrai appareil** (il faudrait relancer le Service et laisser tourner un peu pour voir la liste se peupler dans le dashboard).

## PipeSecurity (2026-09-23)

`PipeServer.CreatePipe()` (NautilyouService) crée désormais le pipe avec une ACL explicite via `NamedPipeServerStreamAcl.Create` (Windows uniquement, repli sur le constructeur classique ailleurs) : `LocalSystem` et `Administrators` en contrôle total, `INTERACTIVE` (toute session de connexion interactive) en lecture/écriture. Volontairement **pas** `Everyone` : un autre compte ou un service réseau ne doit pas pouvoir se faire passer pour le Companion (pair-request, time-request...). Compile (0 erreur). **Testé en `dotnet run` le 2026-09-23 : le Companion se connecte toujours normalement (confirmé par l'utilisateur).** Pas encore testé en vrai service SYSTEM (Session 0), là où l'ACL prend tout son sens.

## TLS / WSS (2026-09-23)

- **Serveur** (`server/src/index.js`) : HTTPS + WSS natif si `TLS_CERT_FILE` et `TLS_KEY_FILE` (PEM) sont définis, sinon HTTP/WS en clair comme avant. `server/gen-cert.sh [hôte-ou-ip]` génère un certificat auto-signé (10 ans, SAN inclus) dans `server/certs/` (ignoré par git). `docker-compose.yml` : variables et volume `./server/certs` prêts, commentés.
- **Client** (`TlsPinning.cs`, nouveau) : certificat signé par une autorité reconnue (ex. Let's Encrypt derrière un domaine) → accepté normalement. Certificat auto-signé → accepté **une seule fois au pairing** (trust on first use), empreinte SHA-256 stockée dans `PairingState.CertSha256`, puis toute connexion (HTTP repli + WebSocket) exige cette empreinte exacte. Le client convertissait déjà `https://` en `wss://`.
- **Testé** (serveur TLS sur :4443 + client .NET jetable) : sans pin ni TOFU → refusé ; TOFU → OK (HTTP 200 + WSS ouvert) ; bon pin → OK ; mauvais pin → refusé.
- Limites : le dashboard (`VITE_API_URL`) et le Companion n'ont pas été adaptés (le dashboard en HTTPS avec certificat auto-signé demande une exception navigateur) ; si le certificat auto-signé est renouvelé, l'appareil doit être ré-appairé. Pas encore testé avec le vrai Service/pairing de bout en bout.

## Test de la page "site bloqué" en HTTPS (2026-09-23)

Testé sur `facebook.com` (liste noire, Chrome) : le blocage fonctionne, mais en HTTPS Chrome affiche `ERR_CONNECTION_REFUSED` et non notre page (rien n'écoute sur 127.0.0.2:443). Notre page et son bouton "Demander l'accès" ne s'affichent qu'en HTTP (port 80). Afficher la page en HTTPS demanderait un certificat racine local installé dans Windows (réglage de sécurité système) : **décision utilisateur : on ne touche pas aux réglages système** (même position que pour la règle DoH du registre). Alternative sans rien installer, non implémentée : notification du Companion "facebook.com est bloqué" + entrée de menu pour demander l'accès. Faux positif rencontré pendant ce test : après une coupure du filtre (watchdog), Chrome/Windows gardent l'IP réelle en cache et le site paraît "plus bloqué" ; `ipconfig /flushdns` + vidage du cache hôte de Chrome règle ça.

## Notification "site bloqué" dans le Companion + affinage "Sites les plus consultés" (2026-09-23)

- **Filtre DNS** (`DnsFilterServer`) : événement `SiteBlocked(entrée)` levé quand un site est bloqué, au plus une fois par minute et par entrée. L'entrée est celle de la liste noire qui a déclenché le blocage (ex. `facebook.com` même si la requête était `www.facebook.com`), sinon le domaine racine en mode liste blanche : l'approbation côté serveur (`requests.js`) retire l'entrée par égalité exacte de la liste noire.
- **Pipe** : messages `blocked-site` (Service vers Companion) et `access-request` (Companion vers Service). Le Service relaie via `OnAccessRequestedAsync`, le même chemin que le bouton de la page de blocage HTTP : la demande arrive dans l'onglet Alertes.
- **Companion** : bulle "Site bloqué, clique ici pour demander l'accès" (le clic envoie la demande) ; le menu de l'icône liste les 5 derniers sites bloqués. Compile (0 erreur), pas encore testé en réel. Le Companion auto-démarré est une copie publiée dans `%LocalAppData%\Nautilyou\Companion` : relancer `install-companion-autostart.ps1` pour qu'il prenne ce code.
- **"~5 min" partout (retour utilisateur)** : normal après un redémarrage du Service (compteurs en mémoire, une tranche de 5 min par site). Corrigé : tranches de **1 minute** au lieu de 5, et compteurs **persistés** dans `LocalState.SiteMinutesToday` (rechargés au démarrage, remis à zéro au changement de jour). Le dashboard affiche `~{count} min`. **Validé en réel le 2026-09-23 (confirmation utilisateur : "Tout a fonctionné")** : minutes du dashboard OK, bulle "site bloqué" reçue, demande d'accès envoyée. Retour utilisateur : la bulle doit revenir au rechargement de la page ; corrigé en passant l'anti-répétition de 1 min à 10 s par entrée et le TTL de la réponse sinkhole de 60 s à 1 s (sinon Windows/Chrome ne redemandent pas le DNS). Limite : Chrome peut garder son propre cache de noms, `ipconfig /flushdns` si besoin.

## Test réel PipeSecurity (vrai service SYSTEM) + TLS/WSS de bout en bout (2026-09-23)

Scénario : serveur en HTTPS sur :4443 (certificat auto-signé de `server/gen-cert.sh`), dashboard pointé dessus (`VITE_API_URL`), `pairing.json` supprimé, Service installé via `install-service.ps1` (LocalSystem, Session 0) puis passé en `Manual` avec `NAUTILYOU_SKIP_DNS_ADAPTER=1` posée uniquement dans la clé de registre du service (pas de redirection du DNS pendant le test), Companion réinstallé (`install-companion-autostart.ps1`) et lancé en session utilisateur.

Résultats vérifiés (fichiers + base) :
- **PipeSecurity validé en vrai service** : le Companion (utilisateur normal) s'est connecté au pipe du Service SYSTEM, a affiché le formulaire de pairing et a transmis la demande (le service a bien appairé).
- **TLS/WSS validé de bout en bout** : pairing via `https://localhost:4443`, `pairing.json` contient `CertSha256` (empreinte épinglée, identique à celle du certificat testé plus tôt), le nouvel appareil est `online=1` avec `last_seen_at` récent, donc le WebSocket WSS est ouvert depuis le service SYSTEM.
- État laissé : service `NautilyouService` installé, `Manual`, en cours d'exécution avec `NAUTILYOU_SKIP_DNS_ADAPTER=1` ; serveur et dashboard en HTTPS. Pour revenir en HTTP/dev : supprimer `pairing.json`, relancer le serveur sans `TLS_*` et le dashboard sans `VITE_API_URL`. `uninstall-service.ps1` retire le service.
- Appareils `FIXE-JIMMY` en doublon dans la base (anciens pairings) : à supprimer depuis le dashboard.

## Installateur Windows (2026-09-23)

Demande utilisateur : un exécutable + un installateur simple. **Fait avec Inno Setup 6** (déjà présent dans `%LocalAppData%\Programs\Inno Setup 6`), dans `client/installer/` :
- `build-installer.ps1 [-Version 1.0.0]` : publie Service et Companion en autonome (self-contained, win-x64, fichier unique compressé, pas besoin de .NET sur la machine cible) dans `staging/`, puis compile `Nautilyou.iss`. Sortie : `output/Nautilyou-Setup-<version>.exe` (staging/output ignorés par git).
- `Nautilyou.iss` : assistant en français, droits admin, installe dans `Program Files\Nautilyou`. Crée le vrai service (`sc create`, LocalSystem, démarrage automatique, relance après crash, démarré à la fin), ajoute le Companion au démarrage de session de tous les utilisateurs (clé `HKLM\...\Run`), supprime l'ancien raccourci de démarrage du script `install-companion-autostart.ps1`, propose de lancer le Companion (qui affiche le formulaire d'appairage URL + code). Mise à jour : arrête et supprime l'ancien service et les processus avant de copier.
- **Désinstallation** : arrête/supprime le service, tue le Companion, **remet le DNS en automatique** (`Set-DnsClientServerAddress -ResetServerAddresses`) et supprime `%ProgramData%\Nautilyou` (appairage local, clé de l'appareil, compteurs ; l'appareil reste listé côté serveur).
- Build vérifié (compilation Inno réussie). **Installation pas encore testée** sur la machine. Points d'attention : le service est en démarrage **automatique** et redirige le DNS (garde-fous : watchdog, verrou mono-instance, restauration à la désinstallation, `install/restore-dns.ps1`) ; réinstaller supprime la clé de registre du service, donc la variable `NAUTILYOU_SKIP_DNS_ADAPTER` du test précédent disparaît. Pas de signature de code : Windows SmartScreen affichera un avertissement "éditeur inconnu". Pas d'icône personnalisée.

## Inventaire des applications installées + sélecteur dans le dashboard (2026-09-23)

Demande utilisateur : la machine envoie sa liste d'applications (la même que "Applications installées" de Windows) et le parent les choisit dans le dashboard au lieu de taper un nom qu'il ne connaît pas forcément.

- **Client** : `InstalledAppsScanner` (nouveau) lit les clés `Uninstall` du registre (HKLM 64/32 bits + HKCU), ignore composants système, mises à jour, runtimes, pilotes et entrées sans exécutable, et retrouve les exécutables de chaque appli (`DisplayIcon`, dossier d'installation ; bruit type unins/setup/update filtré, 12 max). Testé sur la machine de l'utilisateur : 54 applications détectées (Chrome, Steam, VLC, OBS, Discord…). `Worker` rescanne toutes les 6 h et renvoie la liste à chaque nouvelle connexion WebSocket (message `apps`).
- **Application des limites** (`AppLimitEnforcer`) : quand le nom configuré vient de l'inventaire, la comparaison se fait sur les **exécutables exacts** de l'appli (ex. "Google Chrome" → `chrome`, `chrome_proxy`) ; un nom saisi à la main hors inventaire garde l'ancienne comparaison approximative. La config reste une liste de noms (compatibilité avec l'existant).
- **Serveur** : table `device_apps` (JSON par appareil), `GET /api/devices/:id/apps` et `GET /api/children/:id/apps` (union de tous les appareils de l'enfant, pour "Tous les appareils").
- **Dashboard** : `AppPicker` dans `ConfigModal` (limites par app et apps bloquées) : recherche par nom dans la liste reçue de l'appareil, tags supprimables, saisie manuelle possible ("Ajouter « x » manuellement"), message si aucune liste n'a encore été reçue. Vérifié dans le navigateur avec un serveur de test isolé (base temporaire, fausses applications) : recherche, choix et ajout OK.
- **Limites connues** : les applications du Microsoft Store (UWP) ne sont pas dans ces clés du registre, donc non listées ; applications portables (sans installateur) non listées ; le nom affiché peut contenir un numéro de version (ex. "7-Zip 25.01 (x64)") : si l'appli est mise à jour, le nom change et la règle ne correspond plus (l'ancien nom disparaît de la liste) ; le blocage par exécutable termine le process (`Kill`), comme avant.
- **Pas encore testé de bout en bout avec le vrai Service** : il faut relancer le serveur (nouvelle table) et le Service, puis ouvrir la config APPS dans le dashboard.

## Dépôt Git (2026-09-23)

Dépôt **privé** `https://github.com/sy-per/nautilyou`, créé par l'utilisateur (le jeton GitHub de l'outil MCP n'a pas le droit de créer un dépôt ; le push HTTPS depuis la machine fonctionne). Le dépôt local est le dossier `Nautilyou/` lui-même (branche `main`, 1er commit `536248f`). `.gitignore` racine : `node_modules`, `dist`, `bin`/`obj`, `data/`, `*.db*`, `certs/`, `.env`, `installer/staging` et `installer/output`, `.claude/`. Aucun secret versionné. Commits faits avec une identité passée en ligne (`-c user.name`), la config git de la machine n'a pas été modifiée.

## Statut

- **UI mock du dashboard validée par l'utilisateur le 2026-09-23** ("Parfait on part là-dessus"). Détail dans la section UI mock ci-dessus.
- **Serveur (API + DB SQLite) scaffoldé et testé le 2026-09-23** — tous les endpoints CRUD, pairing, bonus/lock instantanés fonctionnels (testés via curl et via le vrai client), **et en conteneur Docker** (build, persistance, WebSocket, tout validé via Docker dans WSL).
- **Dashboard branché sur l'API réelle le 2026-09-23** — plus aucune donnée mock, `dashboard-ui` consomme `server` en direct, testé bout en bout dans le navigateur.
- **Modules de config (WEB/TEMPS/APPS), badge statut vert/rouge, bonus temps libre et réglages "tous les appareils" ajoutés le 2026-09-23** — détail plus haut. Logique d'agrégation du badge (vert si au moins un appareil a accès) confirmée par l'utilisateur.
- **Ajout/suppression d'enfant + fusion du réglage "tous les appareils" dans le sélecteur d'appareil, le 2026-09-23** — testé dans le navigateur.
- **"Ajouter un appareil" câblé avec code de pairing, le 2026-09-23** — testé, d'abord simulé via curl puis avec le vrai client.
- **Client Windows V1 créé et testé en conditions réelles le 2026-09-23** — pairing, suivi de temps, enforcement, blocage de sites basique.
- **Bascule P2P WebRTC → WebSocket, le 2026-09-23** — voir section dédiée ci-dessus, testée en conditions réelles (push de bonus temps reçu en direct côté client).
- **Sécurité du pairing (RSA + défi/réponse) et persistance locale client, le 2026-09-23** — voir section dédiée ci-dessus, testées en conditions réelles (redémarrage du client → fail-safe offline confirmé avant reconnexion WebSocket).
- **Filtrage de sites par DNS local (liste noire + liste blanche), le 2026-09-23** — voir section dédiée ci-dessus. Logique validée en isolation (port non privilégié) ; le fonctionnement réel avec droits admin (port 53 + `netsh`) reste à tester avant un vrai déploiement.
- **Systray, page de blocage, demandes temps/accès, le 2026-09-23** — voir section dédiée. Deux bugs trouvés et corrigés en testant (arrondi des minutes à 0, détection idle remplacée par détection de verrouillage de session).
- **Scission service/companion + limites par app, le 2026-09-23** — voir section dédiée. Architecture en 3 projets (`NautilyouShared`/`NautilyouService`/`NautilyouCompanion`), un bug de désérialisation JSON trouvé et corrigé en testant.
- **Scripts d'installation service + auto-démarrage Companion écrits et testés (garde-fous), le 2026-09-23** — voir section dédiée. Installation réelle laissée à l'utilisateur (nécessite l'admin, qu'il a sur sa machine). Deux bugs corrigés en testant (pairing impossible sans console sur un vrai service, encodage des scripts PowerShell).

- **Installation réelle du service testée en conditions réelles le 2026-09-23** — pairing réussi, DNS réellement reconfiguré par `netsh` pour la première fois (hors sandbox). **Incident réel** : panne DNS totale sur la machine de l'utilisateur suite à un conflit entre la console de pairing restée ouverte et le service redémarré ; résolu immédiatement par l'utilisateur, deux mesures de sécurité ajoutées (`restore-dns.ps1`, filet `ProcessExit`). Voir section dédiée.
- **Interface de pairing dans le Companion implémentée le 2026-09-23** (au lieu de la console du Service) — voir section dédiée juste avant "Statut". Compile (0 erreur), **pas encore testée en conditions réelles**.

- **Bug corrigé le 2026-09-23 : diffusion `pairing-needed` unique en mode console.** `EnsurePairedAsync` (branche interactive, `dotnet run`) ne diffusait le message `pairing-needed` qu'une seule fois avant de bloquer sur `Console.ReadLine()` — un Companion qui se connectait juste après ratait le signal et restait bloqué sur l'écran de chargement. Corrigé avec `ReadLineWithPairingBroadcastAsync` : la lecture console tourne sur un thread séparé (`Task.Run`) pendant qu'on continue de rediffuser `pairing-needed` toutes les 5s.
- **Friction de test constatée le 2026-09-23** : jongler entre 3 terminaux (service, companion, génération de code via curl/Invoke-RestMethod) a été jugé "trop complexe" par l'utilisateur, qui a tout arrêté. **Rappel important : cette friction est un artefact du mode dev (`dotnet run` en console), pas du produit final** — un vrai parent aura juste le Service installé en tâche de fond + le Companion en systray + le dashboard web, sans rien taper en console.
- **Script `server/quick-pairing.ps1` créé le 2026-09-23** pour réduire cette friction de test : démarre le serveur si besoin (dans une fenêtre séparée), crée l'enfant "Papa" s'il n'existe pas, génère un code de pairing et l'affiche directement. Usage : `powershell -ExecutionPolicy Bypass -File server\quick-pairing.ps1` (params optionnels `-ChildName`, `-Port`). **Pas encore testé.**

**`StartupType` du service repassé en `Manual` le 2026-09-23** (confirmé par l'utilisateur), en attendant un durcissement complet (PipeSecurity, TLS/WSS).

Prochaine étape possible : tester le script `quick-pairing.ps1` + le flux de pairing du Companion en conditions réelles (Service + Companion, pairing effacé), repasser le service en StartupType Manual en attendant plus de robustesse, lancer l'auto-démarrage du Companion (sur confirmation), tester la page de blocage en conditions admin réelles, ajouter le TLS/WSS pour la prod, ou sécuriser le pipe (`PipeSecurity`) — à trancher avec l'utilisateur.
