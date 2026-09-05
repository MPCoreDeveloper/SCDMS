# ── HAProxy ACME variant runtime image ──
# Same as the official haproxy:3.0 image plus the openssl CLI used by entrypoint.sh to create
# the temporary bootstrap certificate. The Lua plugin, config template and scripts are mounted
# read-only by docker-compose.acme.yml.
#
# The container keeps the official image's non-root 'haproxy' user. /certs and /webroot are
# pre-created here so that Docker named volumes (see docker-compose.acme.yml) inherit their
# ownership. Binding 80/443 requires the NET_BIND_SERVICE capability (cap_add in compose).

FROM haproxy:3.0

RUN apt-get update \
    && apt-get install -y --no-install-recommends openssl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /certs /webroot \
    && chown haproxy:haproxy /certs /webroot

ENTRYPOINT ["/bin/sh", "/usr/local/etc/haproxy/acme-entrypoint.sh"]

