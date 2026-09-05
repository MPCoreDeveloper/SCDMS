# SCDMS + SharpCoreDB — .NET Aspire sample (gRPC)

This Aspire host runs SharpCoreDB server and SCDMS as one application, the same topology as the
production Docker Compose sample in [`samples/docker/`](../../../samples/docker/README.md):

- **SharpCoreDB server** container (`ghcr.io/mpcoredeveloper/sharpcoredb-server`) — HTTPS gRPC
  on container port 5001, HTTPS REST API on container port 8443.
- **SCDMS** web studio container (`ghcr.io/mpcoredeveloper/scdms`) — plain HTTP on container
  port 8080, auto-wired to the server over **gRPC**.

All SCDMS ⇄ SharpCoreDB data traffic flows over gRPC.

## Prerequisites

- .NET 10 SDK and Docker (with a running Docker engine).
- SharpCoreDB server image: published (`ghcr.io/mpcoredeveloper/sharpcoredb-server`).
- SCDMS image: defaults to `ghcr.io/mpcoredeveloper/scdms:latest`. Until that image is
  published, build it locally from the repository root:

  ```bash
  docker build -t ghcr.io/mpcoredeveloper/scdms:latest .
  ```

## Run

The SharpCoreDB server only speaks TLS and requires a certificate. For a local run, generate a
development certificate and drop it next to this project (`certs/server.pfx`); the AppHost
mounts it automatically when present:

```bash
# from the SCDMS.AppHost folder so ./certs resolves:
cd examples/Aspire/SCDMS.AppHost
dotnet dev-certs https -ep certs/server.pfx -p devonly
dotnet run
```

Optional overrides:

```bash
# different JWT secret for the dev server (min. 32 characters)
SCDMS_SERVER_JWT_SECRET="some-random-32-char-secret!" dotnet run

# pin a specific SharpCoreDB server image tag instead of "latest"
SCDB_IMAGE_TAG="2.0.0.2" dotnet run
```

The Aspire dashboard opens automatically; the resources (`db`, `admin`) and their endpoints are
listed there. Open the SCDMS **http** endpoint to reach the web studio.

> Note: with the self-signed `dotnet dev-certs` certificate, the *browser* will warn about the
> SCDMS endpoint certificate **only if** you open the dashboard/SCDMS over TLS. SCDMS itself
> serves plain HTTP inside the container. Because SCDMS validates the server certificate, an
> end-to-end auto-connect over gRPC in this container-only dev setup requires a certificate that
> is trusted by the SCDMS container — see the TLS section below.

## TLS & production notes

- In **production**, terminate TLS at a reverse proxy holding a publicly trusted certificate
  (Let's Encrypt) and let SCDMS reach the gRPC endpoint through that proxy — exactly what the
  [`samples/docker/`](../../../samples/docker/README.md) compose sample does.
- The SharpCoreDB server always enforces TLS 1.2+ and refuses to start without a certificate
  and a JWT secret (`WithJwtSecret`). For local development you can mount a `dotnet dev-certs`
  PFX as shown above; see the SharpCoreDB
  [`ASPIRE_INTEGRATION.md`](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/server/ASPIRE_INTEGRATION.md)
  for the full server-side development-certificate story.
- Persist SCDMS state by mounting a volume on `/app/data`
  (`scdms.WithVolume(...)`/`WithBindMount(...)`); by default state is ephemeral.

A full end-to-end test guide (both Docker Compose and Aspire) lives in
[`docs/container-and-aspire-guide.md`](../../../docs/container-and-aspire-guide.md).
