#!/bin/sh
# Renders haproxy.cfg.tmpl (placeholders __SCDMS_DOMAIN__ / __GRPC_DOMAIN__) into
# /tmp/haproxy.cfg and starts HAProxy in foreground (-W -db, like the official entrypoint).
set -eu

SCDMS_DOMAIN="${SCDMS_DOMAIN:-scdms.example.com}"
GRPC_DOMAIN="${GRPC_DOMAIN:-scdb.example.com}"

sed -e "s/__SCDMS_DOMAIN__/${SCDMS_DOMAIN}/g" \
    -e "s/__GRPC_DOMAIN__/${GRPC_DOMAIN}/g" \
    /usr/local/etc/haproxy/haproxy.cfg.tmpl > /tmp/haproxy.cfg

exec haproxy -W -db -f /tmp/haproxy.cfg "$@"
