# SCDMS — Sharp Core Database Management System

**SCDMS** is the official database studio for [SharpCoreDB](https://github.com/MPCoreDeveloper/SharpCoreDB) — like SSMS, but for SharpCoreDB. A local-first, cross-platform web UI that runs on your machine and opens in your browser.

- 🔎 Table explorer & metadata browser (columns, indexes, triggers)
- ⚡ SQL editor with multi-statement execution, named parameters, saved queries & history
- 🔀 Local databases (directory or single-file `.scdb`) **and** remote SharpCoreDB servers (gRPC)
- 🛡️ Secure-by-default: HTTPS, SafeWebCore strict A+ headers, no password persistence
- 🪟🐧🍎 Windows, Linux & macOS — no .NET installation required

## Install (one command)

**Windows** (PowerShell):
```powershell
irm https://raw.githubusercontent.com/MPCoreDeveloper/SCDMS/main/install/install.ps1 | iex
```

**Linux / macOS** (bash):
```bash
curl -fsSL https://raw.githubusercontent.com/MPCoreDeveloper/SCDMS/main/install/install.sh | bash
```

Both scripts are idempotent, verify SHA256 checksums, install per-user (no admin/root), and double as the updater: **re-running them updates SCDMS to the latest release.**

After install, start SCDMS from your Start Menu / app launcher, or:

```bash
scdms-open   # starts the server and opens https://localhost:5443
scdms        # starts the server only
```

> **First launch:** SCDMS serves HTTPS with a locally generated, self-signed `localhost` certificate (there is no .NET SDK to piggyback on). Accept the one-time browser warning, or trust the certificate via your OS certificate store. See [docs/usage.md](docs/usage.md).

## Update

- **In-app:** SCDMS checks GitHub Releases at most once per 24h and shows a banner when a newer version exists.
- **CLI:** `scdms --update` (or `scdms --check-update`).
- **Manual:** re-run the install one-liner above.

## Uninstall

```powershell
# Windows
irm https://raw.githubusercontent.com/MPCoreDeveloper/SCDMS/main/install/install.ps1 | iex -ArgumentList '-Uninstall'
```
```bash
# Linux / macOS
curl -fsSL https://raw.githubusercontent.com/MPCoreDeveloper/SCDMS/main/install/install.sh | bash -s -- --uninstall
```

Your databases and settings (in `%LOCALAPPDATA%\SCDMS` / `~/.local/share/SCDMS`) are never touched by (un)installs.

## Migrating from SharpCoreDB.WebViewer

On first start, SCDMS automatically migrates your existing `%LOCALAPPDATA%\SharpCoreDB.WebViewer` data (settings, saved queries, history and databases) to the SCDMS folder. Nothing is deleted.

## Build from source

Requires the .NET 10 SDK:

```bash
git clone https://github.com/MPCoreDeveloper/SCDMS.git
cd SCDMS
dotnet build SCDMS.slnx
dotnet run --project src/SCDMS/SCDMS.csproj
```

Dev launchers: `scripts/launch.ps1` (Windows), `scripts/launch.sh` (Linux/macOS). Smoke test: `scripts/smoke-test.ps1`.

## Release process (maintainers)

1. Tag: `git tag v1.0.0 && git push origin v1.0.0`
2. The [release workflow](.github/workflows/release.yml) publishes self-contained single-file binaries for `win-x64`, `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64` plus `SHA256SUMS.txt` to GitHub Releases.
3. The [Docker workflow](.github/workflows/docker-publish.yml) builds the container image (`ghcr.io/mpcoredeveloper/scdms`) for `linux/amd64` + `linux/arm64`.
4. The [NuGet workflow](.github/workflows/nuget-publish.yml) packs `SCDMS.Aspire.Hosting` and publishes it to NuGet.org.

## Docker & gRPC deployments

SCDMS is container-ready and talks to SharpCoreDB **exclusively over gRPC** in server mode.

**Container defaults:** plain HTTP on `:8080`, bind `0.0.0.0`, data in `/app/data` — TLS is terminated by a reverse proxy that holds a publicly trusted certificate (e.g. Caddy + Let's Encrypt). The SCDMS gRPC client connects to the server through that proxy and validates the public certificate.

```bash
docker build -t ghcr.io/mpcoredeveloper/scdms:local .

# container-mode smoke run (no TLS, bind all interfaces):
docker run --rm -p 8080:8080 \
  -e SCDMS__EnableHttps=false \
  -e SCDMS__BindAddress=0.0.0.0 \
  -e SCDMS__DataDirectory=/app/data \
  -e SCDMS__DefaultServerHost=scdb.example.com \
  -e SCDMS__DefaultServerPort=443 \
  -e SCDMS__DefaultServerAutoConnect=true \
  ghcr.io/mpcoredeveloper/scdms:local
```

A full example (SCDMS + SharpCoreDB server + Caddy reverse proxy with Let's Encrypt) lives in [`samples/docker/`](samples/docker/). Drop-in proxy alternatives with the same topology are [`samples/yarp/`](samples/yarp/) (all-.NET) and [`samples/haproxy/`](samples/haproxy/) (industry-standard HAProxy). Configuration reference for container deployments (env variables, HTTP mode, default gRPC server, data volumes) is in [docs/usage.md](docs/usage.md).

## .NET Aspire

SCDMS ships a `SCDMS.Aspire.Hosting` NuGet package (plus a runnable [AppHost example](examples/Aspire/SCDMS.AppHost/)) that runs SharpCoreDB server + SCDMS as one Aspire application — like pgweb/pgAdmin next to PostgreSQL:

```csharp
var db = builder.AddSharpCoreDB("db").WithServerContainer(); // SharpCoreDB server container
builder.AddSCDMS("admin", db);                               // SCDMS container auto-wired over gRPC
```

Requires the published images `ghcr.io/mpcoredeveloper/sharpcoredb-server` (published) and `ghcr.io/mpcoredeveloper/scdms` (built on `v*` tags; until then `docker build -t ghcr.io/mpcoredeveloper/scdms:latest .`). See [docs/aspire.md](docs/aspire.md) for the design, status and the TLS notes. A step-by-step run & test guide for **both** options (Compose and Aspire) is in [docs/container-and-aspire-guide.md](docs/container-and-aspire-guide.md).

## Documentation

- [Run & test guide — Docker Compose + .NET Aspire](docs/container-and-aspire-guide.md)

- [Usage & configuration](docs/usage.md)
- [.NET Aspire integration (design & status)](docs/aspire.md)
- [Standalone/migration plan](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/viewer/scdms-standalone-plan.md) (in the SharpCoreDB repo)
- [SharpCoreDB documentation](https://github.com/MPCoreDeveloper/SharpCoreDB)

## License

MIT — see [LICENSE](LICENSE). SCDMS is free and open-source software; infrastructure costs are €0 (GitHub-hosted).
