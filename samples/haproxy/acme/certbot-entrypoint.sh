#!/bin/sh
# Certbot companion loop for the HAProxy ACME variant:
#   - issues/renews certificates for SCDMS_DOMAIN and GRPC_DOMAIN via http-01 (webroot),
#   - runs the deploy hook (/bin/sh /hooks/certbot-deploy.sh) after every successful
#     issue/renewal; the hook writes the combined HAProxy PEM,
#   - retries every 12 hours (harmless while certificates are still valid thanks to
#     --keep-until-expiring).
set -eu

: "${ACME_EMAIL:?ACME_EMAIL is required (set it in samples/haproxy/.env)}"
: "${SCDMS_DOMAIN:?SCDMS_DOMAIN is required}"
: "${GRPC_DOMAIN:?GRPC_DOMAIN is required}"

WEBROOT=/webroot
mkdir -p "$WEBROOT"

echo "[certbot] starting companion for ${SCDMS_DOMAIN} + ${GRPC_DOMAIN}"

while true; do
    echo "[certbot] requesting certificates (http-01 via haproxy on :80)"
    certbot certonly \
        --webroot -w "$WEBROOT" \
        -d "$SCDMS_DOMAIN" -d "$GRPC_DOMAIN" \
        --non-interactive \
        --agree-tos \
        --email "$ACME_EMAIL" \
        --keep-until-expiring \
        --deploy-hook "/bin/sh /hooks/certbot-deploy.sh" \
        || echo "[certbot] attempt failed - retrying in 12h (domains must resolve to this host and :80 must be reachable)"

    sleep 12h
done
