# Nautilyou

Contrôle parental auto-hébergé : temps d'écran, plages horaires, filtrage de sites et limites par application.

- **Serveur** (`server/`) : API, WebSocket des appareils, base SQLite. Node.js.
- **Dashboard** (`dashboard-ui/`) : interface parent (React).
- **Client Windows** (`client/`) : service Windows + application de barre des tâches, installateur `.exe`.

## Installer le serveur (Docker)

Prérequis : une machine sur le réseau local avec Docker et Docker Compose.

```bash
git clone https://github.com/sy-per/nautilyou.git
cd nautilyou
cp .env.example .env
```

Édite `.env` et mets dans `SERVER_NAME` l'**adresse IP** de la machine (et éventuellement son nom, séparés par une virgule), par exemple `SERVER_NAME=192.168.1.50`. Puis :

```bash
docker compose up -d --build
```

Le dashboard est disponible sur `https://<adresse-de-la-machine>` (port 443).

### Premier accès : création du compte parent

À la première visite, le dashboard demande de **créer le compte parent** (identifiant et mot de passe de 8 caractères minimum). Ce compte est unique et crée automatiquement une session ; ensuite, chaque visite passe par la page de connexion. Tant que ce compte n'existe pas, n'importe qui pouvant joindre le serveur peut le créer : fais-le tout de suite après l'installation.

Le mot de passe est stocké haché (scrypt), les sessions durent 14 jours, et 5 échecs de connexion consécutifs bloquent temporairement l'adresse. Mot de passe oublié : supprime le compte, puis recrée-le à la prochaine visite (les enfants et appareils ne sont pas touchés) :

```bash
docker compose exec server node -e "const {DatabaseSync}=require('node:sqlite'); const db=new DatabaseSync('/data/nautilyou.db'); db.exec('PRAGMA foreign_keys=ON; DELETE FROM parents')"
```

### HTTPS et certificat auto-signé

Un conteneur nginx est la seule porte d'entrée : il sert le dashboard et relaie l'API et le WebSocket des appareils vers le serveur, qui reste interne. Au premier démarrage il génère un certificat **auto-signé** (valable 10 ans) pour les adresses de `SERVER_NAME`.

- Le navigateur affiche un avertissement la première fois : accepte l'exception.
- Le certificat est conservé dans le volume Docker `nautilyou_certs`. **Ne supprime pas ce volume** : les appareils appairés mémorisent l'empreinte du certificat et refuseraient un nouveau certificat (il faudrait les ré-appairer).
- Si l'adresse IP de la machine change, supprime le volume `nautilyou_certs`, relance, puis ré-appaire les appareils.

### Sauvegarde et mise à jour

Les données (enfants, appareils, activité) sont dans le volume `nautilyou_data`.

```bash
# Sauvegarde de la base (le préfixe "nautilyou_" du volume est le nom du dossier du projet)
docker run --rm -v nautilyou_nautilyou_data:/data -v "$PWD":/backup alpine cp /data/nautilyou.db /backup/nautilyou-backup.db

# Mise à jour
git pull
docker compose up -d --build
```

## Installer le client (Windows)

Lance `Nautilyou-Setup-<version>.exe` sur le PC de l'enfant (droits administrateur). À la fin de l'installation, la fenêtre Nautilyou demande :

1. l'**adresse du serveur** (par exemple `192.168.1.50`) ;
2. un **code à 6 chiffres**, généré dans le dashboard (enfant, puis « Ajouter un appareil »).

L'appairage est mémorisé : il n'est pas redemandé au redémarrage ni lors d'une mise à jour de l'installateur.

Construire l'installateur (Windows, avec .NET SDK et Inno Setup 6) :

```powershell
powershell -ExecutionPolicy Bypass -File client\installer\build-installer.ps1
```

## Développement

Le fichier `nautilyou-dev-context.md` est le journal de conception et de tests (décisions, bugs rencontrés, ce qui reste à faire).
