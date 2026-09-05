#!/bin/sh
# Certbot deploy hook (called after every successful issue/renewal).
# Combines the private key + full chain into the PEM that HAProxy uses (/certs/haproxy.pem).
# HAProxy does not need a signal from here: its entrypoint watches the PEM and triggers a
# graceful reload (SIGUSR2) when the file changes.
set -eu

: "${RENEWED_LINEAGE:?RENEWED_LINEAGE is not set (certbot deploy hook context)}"

cat "$RENEWED_LINEAGE/privkey.pem" "$RENEWED_LINEAGE/fullchain.pem" > /certs/haproxy.pem
echo "[certbot deploy] wrote /certs/haproxy.pem from $RENEWED_LINEAGE"
