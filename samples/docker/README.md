# SCDMS + SharpCoreDB — Docker Compose sample (gRPC + reverse proxy)

This sample runs:

- **SCDMS** — the web studio, serving plain HTTP on `:8080` inside the container.
- **SharpCoreDB server** — the gRPC database server (`:5001`, TLS).
- **Caddy** — reverse proxy that terminates TLS with a publicly trusted
  (Let's Encrypt) certificate for both the SCDMS web UI and the gRPC endpoint.

All SCDMS ⇄ SharpCoreDB data traffic flows over **gRPC**. SCDMS connects to
`https://<GRPC_DOMAIN>:443` and validates the public certificate; the proxy re-encrypts
to the server's internal certificate, so no custom CA is needed inside SCDMS.

## Quick start

1. `cp .env.example .env` and edit the domains + secrets.
2. Put the SharpCoreDB server TLS certificate (e.g. `server.pfx`) in `./server-certs/`.
3. Make sure the container images are available (see below).
4. `docker compose up -d`
5. Open `https://<SCDMS_DOMAIN>` — SCDMS auto-connects to the configured gRPC server.

## Container images

The compose file references published images by default:

- `ghcr.io/mpcoredeveloper/scdms:latest`
- `ghcr.io/mpcoredeveloper/sharpcoredb-server:latest`

Until those images are published (the `docker-publish.yml` workflow builds SCDMS on a
`v*` tag), build them locally by uncommenting the `build:` blocks in `docker-compose.yml`:

```yaml
# for scdms
build:
  context: ../..
  dockerfile: Dockerfile

# for sharpcoredb
build:
  context: /path/to/SharpCoreDB
  dockerfile: src/SharpCoreDB.Server/Dockerfile
```

## gRPC-only note

The SharpCoreDB server exposes gRPC (5001) and an HTTPS management API (8443). SCDMS uses
only gRPC for data access. If you connect SCDMS straight to the server (without the proxy)
inside a private network, either keep the proxy pattern above or supply a certificate that
SCDMS can validate (or terminate at the proxy — see [docs/usage.md](../../docs/usage.md)).
