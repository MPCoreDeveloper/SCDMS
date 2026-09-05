# SCDMS + SharpCoreDB — HAProxy reverse proxy sample

HAProxy is the battle-tested, industry-standard load balancer / reverse proxy. This sample
runs the same topology as [`samples/docker/`](../docker/) and [`samples/yarp/`](../yarp/) but
terminates TLS with **HAProxy** instead of Caddy/YARP:

```text
Browser ──HTTPS (cert on HAProxy)──► haproxy ──HTTP──► scdms:8080          (SCDMS web UI)
SCDMS   ──gRPC https://<GRPC_HOST>──► haproxy ──gRPC/TLS──► sharpcoredb:5001
```

All SCDMS ⇄ SharpCoreDB data traffic flows over **gRPC**. The proxy terminates TLS for both the
SCDMS web UI and the SCDMS→server gRPC channel, then re-encrypts to the SharpCoreDB server's
internal certificate (`ssl verify none`, mirroring Caddy's `tls_insecure_skip_verify`).

## Caddy vs. YARP vs. HAProxy

| | Caddy (`samples/docker/`) | YARP (`samples/yarp/`) | HAProxy (this folder) |
|---|---|---|---|
| Proxy | `caddy:2.9` official image | Your .NET image (`Yarp.ReverseProxy`) | `haproxy:3.0` official image |
| TLS certificates | **Automatic** Let's Encrypt | Manual PFX | Manual **PEM** |
| Config | Caddyfile | C# code | `haproxy.cfg` (templated) |
| Language | Go | 100% .NET/C# | C |
| Sweet spot | Zero-maintenance auto-TLS | Everything in .NET | Industry-standard proxy/LB, huge ecosystem |

Choose HAProxy when you (or your ops team) already standardize on it — it is the most widely
deployed option and supports advanced LB features (health checks, stickiness, routing policies).

## Quick start

1. Make the images available:

   ```bash
   cd <repo-root>/SCDMS
   docker build -t ghcr.io/mpcoredeveloper/scdms:latest .   # until the official image is published
   docker pull ghcr.io/mpcoredeveloper/sharpcoredb-server:2.0.0.2
   ```

2. Configure and create the TLS certificate. HAProxy needs a **PEM** (key + certificate);
   for local testing convert the dev PFX:

   ```bash
   cd samples/haproxy
   cp .env.example .env        # PowerShell: Copy-Item .env.example .env
   mkdir -p server-certs
   dotnet dev-certs https -ep server-certs/server.pfx -p devonly
   openssl pkcs12 -in server-certs/server.pfx -out server-certs/haproxy.pem -nodes -passin pass:devonly
   ```

   Edit `.env`: set `SCDMS_DOMAIN=localhost` and `GRPC_DOMAIN=localhost` for local testing, or
   your real public hostnames in production (the PEM must cover both hostnames).

3. Start everything:

   ```bash
   docker compose up -d
   docker compose ps                       # haproxy, scdms, sharpcoredb all "healthy"
   ```

## Verify

```bash
# HAProxy (stats socket, socat)
docker compose exec haproxy sh -c "echo 'show info' | socat unix-connect:/var/lib/haproxy/admin.sock stdio" | head

# SCDMS health (through the proxy)
docker compose exec scdms curl -fs http://localhost:8080/health

# SharpCoreDB server health (internal self-signed cert = -k)
docker compose exec sharpcoredb curl -fsk https://localhost:8443/api/v1/health

# Logs
docker compose logs -f haproxy scdms
```

- Browser: `https://<SCDMS_DOMAIN>` (accept the self-signed warning when using `localhost`).
- The HAProxy log lines show the SNI-based backend selection (`scdms_ui` / `scdb_grpc`).

## TLS notes

- **SCDMS validates the certificate of the gRPC endpoint it connects to.** In production the
  mounted PEM must be publicly trusted (as with any proxy). With a self-signed `localhost` cert
  the browser warns and SCDMS's automatic gRPC connect cannot validate the proxy cert — the same
  TLS caveat as the other samples (see
  [`docs/container-and-aspire-guide.md`](../../docs/container-and-aspire-guide.md), §5 for a
  green local data-path test).
- The proxy skips certificate validation only towards the *internal* SharpCoreDB server
  (`ssl verify none`). That switch never affects what SCDMS validates.
- The container runs as the non-root `haproxy` user; binding 80/443 needs the
  `NET_BIND_SERVICE` capability (added in `docker-compose.yml`).

## Fully automatic certificates (ACME variant)

`docker-compose.acme.yml` runs the same stack but manages the HAProxy certificate
**automatically** with Let's Encrypt — no manual PEM, no manual renewal:

- **HAProxy** (custom image `./acme`, based on `haproxy:3.0` + `openssl`) loads the
  [janeczku HAProxy ACME Lua plugin](https://github.com/janeczku/haproxy-acme-validation-plugin)
  (`./acme/acme-http01-webroot.lua`) which answers `http-01` challenges on port 80 from the
  shared `/webroot` folder. Its entrypoint creates a temporary self-signed bootstrap cert when
  none exists yet and **gracefully reloads (SIGUSR2)** whenever the PEM file changes.
- **Certbot companion** (`certbot/certbot`) issues/renews the certificate every 12 h for
  `SCDMS_DOMAIN` + `GRPC_DOMAIN` (webroot method). After every successful issue/renewal its
  deploy hook (`./acme/certbot-deploy.sh`) combines `privkey.pem` + `fullchain.pem` into
  `/certs/haproxy.pem`.

Requirements: `SCDMS_DOMAIN` and `GRPC_DOMAIN` must **resolve publicly** to this host and ports
`80`/`443` must be reachable (that is how Let's Encrypt validates ownership). Set `ACME_EMAIL`
in `.env`. Local `localhost` testing still uses the manual compose file above.

```bash
cd samples/haproxy
cp .env.example .env            # set real domains + ACME_EMAIL + secrets
mkdir -p server-certs           # dev PFX for the SharpCoreDB server's internal TLS
dotnet dev-certs https -ep server-certs/server.pfx -p devonly
docker compose -f docker-compose.acme.yml up -d --build
```

Check the flow:

```bash
docker compose -f docker-compose.acme.yml ps          # haproxy, certbot, scdms, sharpcoredb
docker compose -f docker-compose.acme.yml logs -f certbot   # issuance/renewal activity
docker compose -f docker-compose.acme.yml logs haproxy      # "[entrypoint] certificate changed - graceful reload"
# after issuance, the real certificate is live on https://<SCDMS_DOMAIN> and https://<GRPC_DOMAIN>
```

State (live `haproxy.pem`, challenge webroot, certbot account) lives in the Docker named volumes
`haproxy-certs`, `haproxy-webroot` and `letsencrypt`. Reset with
`docker compose -f docker-compose.acme.yml down -v`.

## Stop

```bash
docker compose down        # add -v to also delete the data volumes
```
