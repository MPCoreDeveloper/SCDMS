# SCDMS + SharpCoreDB — YARP reverse proxy sample (all-.NET)

This is an **all-.NET** alternative to the Caddy-based sample in
[`samples/docker/`](../docker/). It runs the same three-part topology but replaces Caddy with a
small **YARP** (`Yarp.ReverseProxy`) proxy that you build from `./proxy`:

```text
Browser ──HTTPS (cert on YARP)──► yarp ──HTTP──► scdms:8080          (SCDMS web UI)
SCDMS   ──gRPC https://<GRPC_HOST>──► yarp ──gRPC/TLS──► sharpcoredb:5001
```

All SCDMS ⇄ SharpCoreDB data traffic flows over **gRPC**. The proxy terminates TLS for both
the SCDMS web UI and the SCDMS→server gRPC channel, then re-encrypts to the SharpCoreDB
server's internal certificate (the server only speaks TLS).

## Caddy vs. YARP — what changes

| | Caddy sample (`samples/docker/`) | YARP sample (this folder) |
|---|---|---|
| Proxy | Official `caddy:2.9` image | Your own .NET image built from `./proxy` |
| TLS certificates | **Automatic** Let's Encrypt per domain | **Manual**: you mount a PFX; cert renewal is your job |
| Language | Go binary + Caddyfile | 100% C# (`Yarp.ReverseProxy` on Kestrel) |
| Customization | Config file | Full programmatic control (routes/clusters/transforms in code) |

Choose YARP when you want everything in .NET and/or deep programmatic proxy control; choose the
Caddy sample when you want zero-maintenance automatic TLS.

## Quick start

1. Make the images available:

   ```bash
   cd <repo-root>/SCDMS
   docker build -t ghcr.io/mpcoredeveloper/scdms:latest .   # until the official image is published
   docker pull ghcr.io/mpcoredeveloper/sharpcoredb-server:2.0.0.2
   ```

2. Configure and create the TLS certificate (local test → use `localhost` in `.env`):

   ```bash
   cd samples/yarp
   cp .env.example .env        # PowerShell: Copy-Item .env.example .env
   mkdir -p server-certs
   dotnet dev-certs https -ep server-certs/server.pfx -p devonly
   ```

   Edit `.env`: set `SCDMS_DOMAIN=localhost` and `GRPC_DOMAIN=localhost` for local testing,
   or your real public hostnames in production (the PFX must then cover both hostnames).

3. Start everything:

   ```bash
   docker compose up -d --build
   docker compose ps                       # yarp, scdms, sharpcoredb all "healthy"
   ```

## Verify

```bash
# YARP proxy health
docker compose exec yarp curl -fs http://localhost:80/health

# SCDMS health (via the proxy, https)
docker compose exec scdms curl -fs http://localhost:8080/health

# SharpCoreDB server health (internal self-signed cert = -k)
docker compose exec sharpcoredb curl -fsk https://localhost:8443/api/v1/health

# Logs
docker compose logs -f yarp scdms
```

- Browser: `https://<SCDMS_DOMAIN>` (accept the self-signed warning when using `localhost`).
- The YARP proxy log shows the routes it registered at startup.

## TLS notes

- **SCDMS validates the certificate of the gRPC endpoint it connects to.** In production the
  mounted PFX must be publicly trusted (as with any proxy). With a self-signed `localhost` cert
  the browser warns and SCDMS's automatic gRPC connect cannot validate the proxy cert — that is
  the same TLS caveat as the Caddy sample (see
  [`docs/container-and-aspire-guide.md`](../../docs/container-and-aspire-guide.md), §5 for a
  green local data-path test).
- The proxy deliberately **skips** certificate validation towards the internal SharpCoreDB
  server (`DangerousAcceptAnyServerCertificate`), exactly like the Caddy sample's
  `tls_insecure_skip_verify`. That switch only affects the proxy↔server leg.
- The proxy runs as a **non-root** user inside the container; binding ports `80`/`443` needs the
  `NET_BIND_SERVICE` capability (added via `cap_add` in `docker-compose.yml`). Map higher ports
  (`YARP_HTTP_PORT`/`YARP_HTTPS_PORT`) when capabilities cannot be granted.

## Stop

```bash
docker compose down        # add -v to also delete the data volumes
```
