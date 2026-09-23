#!/bin/sh
# Genere un certificat auto-signe au premier demarrage (10 ans) et le conserve dans le volume /certs :
# tant que ce volume existe, l'empreinte ne change pas, donc les appareils deja appaires (qui epinglent
# cette empreinte) continuent de se connecter. SERVER_NAME = adresse(s) IP ou nom(s) de la machine,
# separes par des virgules (ex. "192.168.1.50,nautilyou.local").
set -e

if [ -f /certs/server.crt ] && [ -f /certs/server.key ]; then
    echo "Certificat existant conserve (/certs/server.crt)."
    exit 0
fi

NAME="${SERVER_NAME:-localhost}"
SAN="DNS:localhost,IP:127.0.0.1"
FIRST=""
for item in $(echo "$NAME" | tr ',' ' '); do
    [ -z "$FIRST" ] && FIRST="$item"
    case "$item" in
        *[!0-9.]*) SAN="$SAN,DNS:$item" ;;
        *) SAN="$SAN,IP:$item" ;;
    esac
done

mkdir -p /certs
openssl req -x509 -newkey rsa:2048 -nodes -days 3650 -subj "/CN=${FIRST:-localhost}" \
    -addext "subjectAltName=$SAN" \
    -keyout /certs/server.key -out /certs/server.crt 2>/dev/null
chmod 600 /certs/server.key
echo "Certificat auto-signe genere pour : $SAN"
