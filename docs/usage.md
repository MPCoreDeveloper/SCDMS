# SCDMS — Primary Studio Tool

**This is the recommended database studio for SharpCoreDB.** It replaces the legacy `SharpCoreDB.Viewer` Avalonia desktop application, which is now **deprecated**.

SCDMS is a local-first Razor Pages application for inspecting and operating SharpCoreDB databases with secure defaults.

## Key capabilities

- Local connection mode (directory or single-file)
- Network server connection mode (SharpCoreDB gRPC server)
- SafeWebCore strict A+ security profile
- SQL editor with named parameter JSON payloads
- Result grid for executed SELECT statements
- Table explorer and metadata browser (columns, indexes, triggers)
- Transaction controls (begin, commit, rollback)
- Saved query library with scoped visibility per connection target
- Query execution history with success/failure status
- Workspace import/export as JSON

## Built-in databases

The viewer ships with one default database and two sample databases:

| Database | Purpose | Created when |
|---|---|---|
| `scdb` | Default scratch database with a small `welcome` table | Automatically on first launch |
| `contoso` | Retail sample (customers, products, orders, order items, inventory) | On demand (sidebar **Database Actions** or **File** menu) |
| `adventureworks` | Cycles manufacturer sample (products, customers, sales orders, territories) | On demand (sidebar **Database Actions** or **File** menu) |

**Password:** all built-in databases are created with the default password **`scdb`**.
You need it when reconnecting manually (recent connection profiles intentionally do not persist passwords).

**Storage location:** `%LOCALAPPDATA%\SCDMS\Data\<name>` (e.g. `C:\Users\<you>\AppData\Local\Scdms\Data\contoso`).

**Changing the defaults:** edit `appsettings.json`:

```json
"SCDMS": {
  "DefaultDatabaseName": "scdb",
  "DefaultDatabasePassword": "scdb",
  "DefaultDatabasePath": "",
  "SampleDatabasesDirectory": ""
}
```

- `DefaultDatabasePassword` applies to newly created built-in databases. Existing databases keep the password they were created with — delete the database folder to re-create it with a new password.
- `DefaultDatabasePath` overrides the storage folder of the default database.
- `SampleDatabasesDirectory` overrides the root folder for all built-in databases.

## Container deployment (Docker) & environment variables

SCDMS can run as a container. In container mode SCDMS serves **plain HTTP** (default port `8080`) and a reverse proxy terminates TLS, and SCDMS connects to a SharpCoreDB server **over gRPC** (its server connection mode uses `SharpCoreDB.Client`/gRPC exclusively; the HTTPS management API of the server is not used for data traffic).

All settings are configured through environment variables using the `SCDMS__` prefix (the `SCDMS` configuration section). Examples: `SCDMS__EnableHttps=false`, `SCDMS__DefaultServerHost=scdb.example.com`.

| Environment variable | Default | Description |
|---|---|---|
| `SCDMS__EnableHttps` | `true` | Desktop mode: serve HTTPS with the generated self-signed localhost certificate. Set `false` in containers; a reverse proxy terminates TLS. |
| `SCDMS__HttpsPort` | `5443` | Port used when `EnableHttps=true`. |
| `SCDMS__HttpPort` | `8080` | Port used when `EnableHttps=false` (container/HTTP mode). |
| `SCDMS__BindAddress` | `localhost` | Interface to bind. Use `0.0.0.0` in containers. |
| `SCDMS__UseForwardedHeaders` | `false` | Honor `X-Forwarded-Proto`/`X-Forwarded-For`. Enable only behind a trusted reverse proxy. |
| `SCDMS__DataDirectory` | *(OS default)* | Overrides the SCDMS data root (settings, saved queries/history, built-in databases, certificates). Mount a volume here in containers. |
| `SCDMS__DefaultServerHost` | *(empty)* | SharpCoreDB gRPC server host to prefill/auto-connect to. When set, SCDMS skips creating the local `scdb` scratch database. |
| `SCDMS__DefaultServerPort` | `5001` | gRPC port. Use `443` when the gRPC endpoint is fronted by a TLS-terminating reverse proxy. |
| `SCDMS__DefaultServerDatabase` | `master` | Database name for the default server connection. |
| `SCDMS__DefaultServerUsername` | `anonymous` | Username for the default server connection. |
| `SCDMS__DefaultServerPassword` | *(empty)* | Password for the default server connection. |
| `SCDMS__DefaultServerUseSsl` | `true` | TLS for the gRPC channel. Keep `true`; disable only on trusted plaintext networks. |
| `SCDMS__DefaultServerPreferHttp3` | `true` | Prefer HTTP/3 (QUIC) when the server supports it. |
| `SCDMS__DefaultServerAutoConnect` | `false` | Auto-connect to the default server when the UI opens. |

### Reverse proxy & gRPC TLS

The SharpCoreDB server exposes gRPC over **TLS only** (`Server__Security__TlsEnabled`). Recommended deployment: put a reverse proxy (Caddy/nginx) in front that holds a **publicly trusted** certificate (Let's Encrypt) for the gRPC hostname and the SCDMS hostname:

- Browser → `https://scdms.example.com` → SCDMS (`http://scdms:8080`)
- SCDMS → `https://scdb.example.com:443` (gRPC) → SharpCoreDB server gRPC (`:5001`)

SCDMS validates the public certificate, so no custom CA or certificate-skip logic is needed. A complete, runnable sample (SCDMS + SharpCoreDB server + Caddy) is in [`samples/docker/`](../samples/docker/).

### Persistence

Mount a volume at the `SCDMS__DataDirectory` location (`/app/data` in the official image) to persist settings, saved queries/history and built-in databases across container restarts.

### .NET Aspire integration

A `SCDMS.Aspire.Hosting` NuGet package plus a runnable [AppHost example](../examples/Aspire/SCDMS.AppHost/) run SharpCoreDB server + SCDMS as one .NET Aspire application (all SCDMS ⇄ SharpCoreDB traffic over gRPC). Design, status and the TLS-in-development notes: [docs/aspire.md](aspire.md).

## Security posture

SCDMS is configured secure-by-default:

- HTTPS-only endpoint binding
- Session cookie with HttpOnly, Secure, and SameSite=Lax
- SafeWebCore strict A+ headers enabled
- CSP nonce support in layout
- No password persistence in recent connection profiles

## Connection modes

### Local mode

Use this when the viewer runs directly against local SharpCoreDB storage:

- `LocalDatabasePath`
- `LocalStorageMode` (Directory or SingleFile)
- `LocalReadOnly`
- `Password`

### Server mode

Use this when connecting to SharpCoreDB server:

- `ServerHost`
- `ServerPort`
- `ServerDatabase`
- `ServerUsername`
- `ServerUseSsl`
- `ServerPreferHttp3`
- `Password`

## SQL editor and parameters

The SQL editor supports multiple statements in one execution.

Use the **Parameters (JSON object)** field for named parameters:

```json
{
  "@id": 10,
  "@name": "Alice"
}
```

Supported JSON-to-parameter conversions include:

- `null`
- `bool`
- numeric values (`int`, `long`, `decimal`, `double`)
- `string`
- arrays

## Transactions

Transaction controls are available in the SQL panel:

- **Begin** starts a transaction for the current session
- **Commit** commits active transaction
- **Rollback** rolls back active transaction

When a transaction is active, query execution reuses the same transaction-scoped connection.

## Saved queries and history scopes

Saved queries and history are scoped by connection target:

- Global items (no target key) are always visible
- Target-specific items are visible only when connected to that target

Scope examples:

- local: `local:c:\data\mydb`
- server: `server:localhost:5001/master`

## Workspace import/export

The viewer can export/import query workspace state as JSON:

- Saved queries
- Query history

Use the **Workspace Import/Export** panel:

1. Export to JSON
2. Copy payload for backup
3. Paste payload and import when restoring

## Persistence paths

The viewer stores local user data under:

`%LOCALAPPDATA%\SCDMS\`

Files:

- `settings.json` (recent connections)
- `query-workspace.json` (saved queries and history)

In container deployments the root is overridden with `SCDMS__DataDirectory` (see the environment-variable table above).

## Build and run

From repository root:

```powershell
dotnet build src/SCDMS/SCDMS.csproj
dotnet run --project src/SCDMS/SCDMS.csproj
```

## Operational notes

- Keep TLS enabled for server mode in production.
- Use strong database/server passwords.
- Prefer scoped saved queries per target to avoid accidental cross-environment execution.
- Clear history periodically if it may contain sensitive statement previews.


