#!/bin/sh
# HAProxy entrypoint for the ACME variant:
#   1) renders haproxy.cfg.tmpl (__SCDMS_DOMAIN__ / __GRPC_DOMAIN__) to /tmp/haproxy.cfg
#   2) creates a temporary self-signed certificate if /certs/haproxy.pem does not exist yet
#      (HAProxy needs a cert to boot; the certbot companion replaces it with a real one)
#   3) starts HAProxy in master-worker mode (-W -db) and watches the PEM file: on change it
#      sends SIGUSR2 (graceful reload re-reading the certificate) — no downtime.
set -eu

SCDMS_DOMAIN="${SCDMS_DOMAIN:-scdms.example.com}"
GRPC_DOMAIN="${GRPC_DOMAIN:-scdb.example.com}"
CERT_PATH="${HAPROXY_PEM_PATH:-/certs/haproxy.pem}"
CHECK_INTERVAL="${HAPROXY_RELOAD_INTERVAL:-10}"

# 1) render configuration
sed -e "s/__SCDMS_DOMAIN__/${SCDMS_DOMAIN}/g" \
    -e "s/__GRPC_DOMAIN__/${GRPC_DOMAIN}/g" \
    /usr/local/etc/haproxy/haproxy.cfg.tmpl > /tmp/haproxy.cfg

# 2) bootstrap certificate (only when the certbot companion has not written one yet)
if [ ! -f "$CERT_PATH" ]; then
    echo "[entrypoint] no certificate at $CERT_PATH - creating a temporary self-signed one (certbot will replace it)."
    tmpdir="$(mktemp -d)"
    openssl req -x509 -newkey rsa:2048 -nodes \
        -keyout "$tmpdir/key.pem" -out "$tmpdir/cert.pem" \
        -days 30 \
        -subj "/CN=${SCDMS_DOMAIN}" \
        -addext "subjectAltName=DNS:${SCDMS_DOMAIN},DNS:${GRPC_DOMAIN}" 2>/dev/null
    cat "$tmpdir/key.pem" "$tmpdir/cert.pem" > "$CERT_PATH"
    rm -rf "$tmpdir"
fi

# 3) start HAProxy (master-worker: SIGUSR2 = graceful reload)
haproxy -W -db -f /tmp/haproxy.cfg &
HAPROXY_PID=$!

last_mtime="$(stat -c %Y "$CERT_PATH")"

forward_shutdown() {
    echo "[entrypoint] shutting down (signal to haproxy pid $HAPROXY_PID)"
    kill -TERM "$HAPROXY_PID" 2>/dev/null || true
    exit 0
}
trap forward_shutdown TERM INT

while kill -0 "$HAPROXY_PID" 2>/dev/null; do
    current_mtime="$(stat -c %Y "$CERT_PATH")"
    if [ "$current_mtime" != "$last_mtime" ]; then
        echo "[entrypoint] certificate changed - graceful reload (SIGUSR2)"
        kill -USR2 "$HAPROXY_PID" 2>/dev/null || true
        last_mtime="$current_mtime"
    fi
    sleep "$CHECK_INTERVAL"
done

echo "[entrypoint] haproxy exited unexpectedly"
exit 1
