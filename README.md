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

## Documentation

- [Usage & configuration](docs/usage.md)
- [Standalone/migration plan](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/viewer/scdms-standalone-plan.md) (in the SharpCoreDB repo)
- [SharpCoreDB documentation](https://github.com/MPCoreDeveloper/SharpCoreDB)

## License

MIT — see [LICENSE](LICENSE). SCDMS is free and open-source software; infrastructure costs are €0 (GitHub-hosted).
