#!/bin/sh
# Genere un certificat auto-signe (valable 10 ans) pour le mode TLS du serveur Nautilyou.
# Usage : ./gen-cert.sh [nom-ou-ip-du-serveur] (par defaut localhost)
# Le client epingle l'empreinte de ce certificat au pairing (voir TlsPinning cote client).
HOST="${1:-localhost}"
mkdir -p certs
case "$HOST" in
  *[!0-9.]*) SAN="DNS:$HOST" ;;
  *) SAN="IP:$HOST" ;;
esac
openssl req -x509 -newkey rsa:2048 -nodes -days 3650 -subj "/CN=$HOST" \
  -addext "subjectAltName=$SAN,DNS:localhost,IP:127.0.0.1" \
  -keyout certs/server.key -out certs/server.crt
echo "Certificat genere dans certs/ (server.crt / server.key)"
