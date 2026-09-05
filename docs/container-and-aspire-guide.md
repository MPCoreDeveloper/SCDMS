# SCDMS + SharpCoreDB in containers — run & test guide

This guide explains how to run **SCDMS** (the web database studio) together with a
**SharpCoreDB server** using the two container-based options the project ships, and how to
**test the whole thing yourself**:

| Option | What you get | Where it lives | Best for |
|---|---|---|---|
| **A. Docker Compose** | SharpCoreDB server + SCDMS + TLS-terminating reverse proxy | [`samples/docker/`](../samples/docker/) (Caddy, auto-TLS) — alternatives: [`samples/yarp/`](../samples/yarp/) (.NET) and [`samples/haproxy/`](../samples/haproxy/) | Production-style deployments; teams with real domain names |
| **B. .NET Aspire** | SharpCoreDB server + SCDMS as one Aspire app (Aspire dashboard, resources, endpoints) | `SCDMS.Aspire.Hosting` package + [`examples/Aspire/SCDMS.AppHost`](../examples/Aspire/SCDMS.AppHost/) | Local development & cloud-native orchestration |

Both topologies share the same data path:

```text
Browser ──► SCDMS (web UI) ──gRPC/TLS──► SharpCoreDB server
                    │                          │
              plain HTTP :8080            gRPC :5001  (TLS only)
              (TLS at reverse proxy       + HTTPS API :8443
               in production)
```

All SCDMS ⇄ SharpCoreDB data traffic flows over **gRPC**. SCDMS never talks to the server's
HTTPS management API.

---

## 1. Read this first: how TLS works (it decides what you can test)

- The SharpCoreDB server **only speaks TLS 1.2+** and refuses to start without a certificate
  and a JWT secret (`Server__Security__*` settings).
- SCDMS runs **plain HTTP on port 8080** inside a container. In production a reverse proxy
  terminates TLS for the browser.
- When SCDMS connects to the server over gRPC it **validates the server's certificate against
  the OS trust store**. No "skip certificate verification" switch exists.

Consequences for testing:

| Scenario | Full end-to-end (SCDMS auto-connect) | Why |
|---|---|---|
| Compose with **public domain** | ✅ Works | Caddy holds a publicly trusted (Let's Encrypt) cert; SCDMS validates it |
| Compose/Aspire with `localhost` + self-signed dev cert | ⚠️ Partial | Browser warns; SCDMS cannot validate the self-signed server cert |
| Server container + SCDMS **run on the host** with a **trusted** dev cert | ✅ Works | `dotnet dev-certs https --trust` adds the dev CA to the OS store; SCDMS on the host validates it |

So: use a **public domain** for a fully green Compose test, or use a **trusted local dev
certificate** with SCDMS running on the host for a green data-path test. Both recipes are below.

---

## 2. Prerequisites

- **Docker** (Docker Desktop or a Linux engine) with the `docker compose` plugin, engine running.
- **.NET 10 SDK** (the repo's `global.json` pins `10.0.400`).
- Ports free: `80`/`443` (Compose/Caddy), `5001`/`8443` (server), `8080` (SCDMS),
  plus dynamically allocated Aspire ports.
- Container images:

  - `ghcr.io/mpcoredeveloper/sharpcoredb-server` — **published** (tag `2.0.0.2` / `latest`).
  - `ghcr.io/mpcoredeveloper/scdms` — built on `v*` tags. **Not published yet** → build locally:

    ```bash
    cd <repo-root>/SCDMS
    docker build -t ghcr.io/mpcoredeveloper/scdms:latest .
    ```

---

## 3. Option A — Docker Compose (production sample)

The primary sample in [`samples/docker/`](../samples/docker/) runs SharpCoreDB server, SCDMS and
**Caddy** (automatic Let's Encrypt TLS). Two drop-in alternatives with the identical topology and
wiring — but manual certificate provisioning instead of automatic TLS — are in
[`samples/yarp/`](../samples/yarp/) (all-.NET) and [`samples/haproxy/`](../samples/haproxy/)
(industry-standard HAProxy).

```text
Browser ──HTTPS (public cert)──► Caddy ──HTTP──► scdms:8080
SCDMS   ──gRPC https://<GRPC_DOMAIN>──► Caddy ──gRPC/TLS──► sharpcoredb:5001
```

### 3.1 Configure

```bash
cd samples/docker
cp .env.example .env        # PowerShell: Copy-Item .env.example .env
```

Edit `.env`:

| Variable | Value |
|---|---|
| `SCDMS_DOMAIN` | public hostname for the SCDMS UI, e.g. `scdms.example.com` |
| `GRPC_DOMAIN` | public hostname for the gRPC endpoint, e.g. `scdb.example.com` |
| `ACME_EMAIL` | your e-mail (Let's Encrypt notifications) |
| `SERVER_TLS_CERT_PATH` | `/app/certs/server.pfx` (keep default) |
| `SERVER_JWT_SECRET` | **random secret ≥ 32 characters** |
| `SDB_USERNAME` / `SDB_PASSWORD` | login SCDMS uses against the server |

### 3.2 Provide the server certificate

Place the server TLS certificate in `./server-certs/`. For testing you can create a development
PFX (see the SharpCoreDB
[`QUICKSTART.md`](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/server/QUICKSTART.md)
for the authoritative dev-cert instructions):

```bash
mkdir -p server-certs
dotnet dev-certs https -ep server-certs/server.pfx -p devonly --trust
```

> A self-signed dev certificate only works for **local** testing. For the fully working Compose
> experience you need a real public domain so Let's Encrypt can issue a publicly trusted
> certificate — then SCDMS can validate it.

### 3.3 Start & verify

```bash
docker compose up -d
docker compose ps                      # all three services "healthy"
```

Health checks (run from `samples/docker/`):

```bash
# SCDMS internal health (inside its container)
docker compose exec scdms curl -fs http://localhost:8080/health

# SharpCoreDB server health (inside its container, self-signed = -k)
docker compose exec sharpcoredb curl -fsk https://localhost:8443/api/v1/health

# Logs
docker compose logs -f scdms
```

Expected results:

- `docker compose ps` shows `caddy`, `scdms`, `sharpcoredb` all `healthy` (give it ~30-60 s).
- Open `https://<SCDMS_DOMAIN>` — SCDMS UI loads and, with public domains, auto-connects to the
  server (left sidebar shows the `master` database and/or the databases list).
- `sharpcoredb` log line: `🚀 Primary protocol (flagship): gRPC … Endpoint: https://0.0.0.0:5001`.

### 3.4 Local-only smoke test (no public domain)

Point the browser at `https://localhost` instead and accept the browser warning. The
**containers** will still be healthy and the SCDMS UI reachable; SCDMS's automatic gRPC connect
may fail certificate validation — that is expected and is exactly the TLS caveat above. Use the
full end-to-end recipe (§5) for a green data-path test.

### 3.5 Stop

```bash
docker compose down        # add -v to also delete the data volumes
```

### 3.6 Proxy alternatives: YARP & HAProxy

Prefer a different proxy than Caddy? Two drop-in samples with the identical wiring are included:

**YARP — all-.NET** ([`samples/yarp/`](../samples/yarp/)): builds a small `Yarp.ReverseProxy`
2.3.0 proxy on Kestrel (~150 lines of C#, host-based routes). TLS is terminated with a mounted
PFX; the proxy skips certificate validation only towards the *internal* server
(`DangerousAcceptAnyServerCertificate`, mirroring Caddy's `tls_insecure_skip_verify`).

```bash
cd samples/yarp
cp .env.example .env                       # set SCDMS_DOMAIN/GRPC_DOMAIN (localhost for local tests)
mkdir -p server-certs
dotnet dev-certs https -ep server-certs/server.pfx -p devonly
docker compose up -d --build               # builds the yarp proxy image
```

**HAProxy — industry standard** ([`samples/haproxy/`](../samples/haproxy/)): the most widely
deployed proxy/load balancer; SNI-based routing in `haproxy.cfg`. TLS is terminated with a
mounted **PEM** (private key + certificate):

```bash
cd samples/haproxy
cp .env.example .env                       # set SCDMS_DOMAIN/GRPC_DOMAIN (localhost for local tests)
mkdir -p server-certs
dotnet dev-certs https -ep server-certs/server.pfx -p devonly
openssl pkcs12 -in server-certs/server.pfx -out server-certs/haproxy.pem -nodes -passin pass:devonly
docker compose up -d
```

For **fully automatic** Let's Encrypt certificates the same folder ships an ACME variant
(`docker-compose.acme.yml`): HAProxy answers `http-01` challenges via the janeczku Lua plugin, a
certbot companion issues/renews the certificate, and the entrypoint reloads gracefully when the
PEM changes. Requires publicly reachable domains + ports 80/443 — see
[`samples/haproxy/README.md`](../samples/haproxy/README.md).

Caddy is the only proxy with certificate management built in; the HAProxy ACME variant automates
it as well. SCDMS always validates the proxy's *public* certificate — for a fully green local
data-path test use the trusted-dev recipe (§5). Full details:
[`samples/yarp/README.md`](../samples/yarp/README.md) and
[`samples/haproxy/README.md`](../samples/haproxy/README.md).

---

## 4. Option B — .NET Aspire (SCDMS.Aspire.Hosting)

The AppHost example in
[`examples/Aspire/SCDMS.AppHost`](../examples/Aspire/SCDMS.AppHost/) is backed by the
**`SCDMS.Aspire.Hosting`** NuGet package (`AddSharpCoreDB` + `AddSCDMS` + `WithGrpcReference`).
You can use the same package in your own Aspire app.

### 4.1 Prepare images & certificate

```bash
cd <repo-root>/SCDMS

# 1) SCDMS image (until the official image is published)
docker build -t ghcr.io/mpcoredeveloper/scdms:latest .

# 2) Pull the published server image
docker pull ghcr.io/mpcoredeveloper/sharpcoredb-server:2.0.0.2

# 3) Development certificate for the server container
cd examples/Aspire/SCDMS.AppHost
mkdir -p certs
dotnet dev-certs https -ep certs/server.pfx -p devonly
```

The AppHost automatically mounts `certs/server.pfx` when present and points
`Server__Security__TlsCertificatePath` at it. It also uses a dev JWT secret — override with the
`SCDMS_SERVER_JWT_SECRET` environment variable if you want your own (≥ 32 chars).

### 4.2 Run the AppHost

```bash
dotnet run     # from examples/Aspire/SCDMS.AppHost
```

The Aspire dashboard opens in the browser. Expect:

- Two resources: **`db`** (SharpCoreDB server; endpoints `grpc`, `https`) and **`admin`**
  (SCDMS; endpoint `http`), both **Running**.
- Click `admin` → open the **http** endpoint to see the SCDMS web UI.
- SCDMS has received the gRPC link as environment variables. Verify:

```bash
# find the SCDMS container created by Aspire
docker ps --format '{{.Names}}\t{{.Image}}' | grep scdms
docker inspect <container-name> --format '{{range .Config.Env}}{{println .}}{{end}}' | grep '^SCDMS__'
```

You should see `SCDMS__DefaultServerHost`, `SCDMS__DefaultServerPort`,
`SCDMS__DefaultServerUseSsl=true`, `SCDMS__DefaultServerAutoConnect=true` plus the container
defaults (`SCDMS__EnableHttps=false`, `SCDMS__DataDirectory=/app/data`, …).

> With the self-signed dev certificate, SCDMS inside the container cannot validate the server
> certificate, so the *automatic* gRPC connect may fail in this pure container dev setup. The
> wiring is correct (see the environment variables above); use §5 for a green data-path test.

### 4.3 Stop

Stop the AppHost with `Ctrl+C` in the terminal. Containers are removed automatically
(`docker ps` afterwards should no longer list them).

---

## 5. Verify the wiring end-to-end (recommended local recipe)

This gives a **green** data-path test without needing a public domain: the server runs in the
published container, SCDMS runs on your machine and validates the server's dev certificate
because you trust it on the host.

> Run the commands below from `samples/docker/` (where §3.2 created `server-certs/server.pfx`),
> or change the `-v` source so it points at whichever folder holds your PFX (e.g. the AppHost
> `certs/` directory from §4.1).

```bash
# 1) Server container on host ports, using the dev PFX from §3.2/§4.1
docker run -d --name scdb-test \
  -p 5001:5001 -p 8443:8443 \
  -e Server__Security__JwtSecretKey="some-random-secret-of-at-least-32-chars!" \
  -e Server__Security__TlsCertificatePath=/certs/server.pfx \
  -e Server__SystemDatabases__Enabled=true \
  -v "$(pwd)/server-certs:/certs:ro" \
  ghcr.io/mpcoredeveloper/sharpcoredb-server:2.0.0.2

# 2) Health check the server
curl -fsk https://localhost:8443/api/v1/health

# 3) Trust the dev certificate on the host (once)
dotnet dev-certs https --trust
```

Then start SCDMS from source with the default-server settings pointing at the container:

```bash
# PowerShell
$env:SCDMS__DefaultServerHost="localhost"
$env:SCDMS__DefaultServerPort="5001"
$env:SCDMS__DefaultServerAutoConnect="true"
dotnet run --project src/SCDMS/SCDMS.csproj
```

```bash
# bash
export SCDMS__DefaultServerHost=localhost
export SCDMS__DefaultServerPort=5001
export SCDMS__DefaultServerAutoConnect=true
dotnet run --project src/SCDMS/SCDMS.csproj
```

Open `https://localhost:5443` (accept SCDMS's own self-signed UI certificate once):

- The status bar shows a **connected** server session (`localhost:5001/master`).
- The sidebar lists the **`master`** database.
- Run `SELECT * FROM ...` in the SQL editor to confirm gRPC query execution.

Clean up when done: `docker rm -f scdb-test`.

---

## 6. Use `SCDMS.Aspire.Hosting` in your own AppHost

Once published, add the package to your Aspire host:

```bash
dotnet add package SCDMS.Aspire.Hosting
```

```csharp
using Scdms.Aspire.Hosting;
using SharpCoreDB.Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var db = builder.AddSharpCoreDB("db")
    .WithServerContainer()
    .WithJwtSecret("your-random-secret-of-at-least-32-chars");

builder.AddSCDMS("admin", db); // SCDMS container auto-wired to the server over gRPC

builder.Build().Run();
```

`AddSCDMS` without the `db` argument starts SCDMS standalone (no default server). Both overloads
accept an optional `imageTag` and a fixed host `port` for the HTTP endpoint.

---

## 7. Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| `sharpcoredb` restarts / never healthy | No certificate or no JWT secret | Check `docker compose logs sharpcoredb`. Generate the PFX (`dotnet dev-certs https -ep server-certs/server.pfx -p devonly`), keep `SERVER_JWT_SECRET` ≥ 32 chars. If the PFX cannot be loaded, see the server certificate docs (password/format). |
| `scdms` unhealthy | Wrong container env or data volume permissions | Check `docker compose logs scdms`. Required: `SCDMS__EnableHttps=false`, `SCDMS__BindAddress=0.0.0.0`, writable `/app/data` (container runs as uid 1000). |
| Caddy logs certificate errors | Domain does not resolve publicly, or ports 80/443 blocked | Use real public DNS + open ports for Let's Encrypt, or switch to the local `localhost` smoke test (§3.4). |
| SCDMS UI opens but shows a server connection error | SCDMS cannot validate a self-signed server certificate | Expected with self-signed dev certs. Use a public-domain proxy (Option A) or the trusted local recipe (§5). |
| Aspire resources run but SCDMS does not connect | Same certificate validation + wrong `SCDMS__DefaultServer*` | Verify env via `docker inspect` (§4.2); for a green connect use §5. |
| `dotnet` uses the wrong SDK version | Machine default is not .NET 10 | The repo's `global.json` requires a .NET 10 SDK (10.0.400). Install it; VS Code/`dotnet` pick it up automatically. |
| `docker compose`/Aspire says image not found for `scdms` | Official SCDMS image not published yet | Build it locally: `docker build -t ghcr.io/mpcoredeveloper/scdms:latest .` |

---

## 8. Related documentation

- [`docs/aspire.md`](aspire.md) — .NET Aspire design & status (issue #10)
- [`docs/usage.md`](usage.md) — SCDMS configuration, environment variables, connection modes
- [`samples/docker/README.md`](../samples/docker/README.md) — Compose sample notes (Caddy)
- [`samples/yarp/README.md`](../samples/yarp/README.md) — Compose sample notes (YARP, all-.NET)
- [`samples/haproxy/README.md`](../samples/haproxy/README.md) — Compose sample notes (HAProxy, industry standard)
- [`examples/Aspire/SCDMS.AppHost/README.md`](../examples/Aspire/SCDMS.AppHost/README.md) — AppHost example notes
- SharpCoreDB side: [`ASPIRE_INTEGRATION.md`](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/server/ASPIRE_INTEGRATION.md) and [`QUICKSTART.md`](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/server/QUICKSTART.md)



