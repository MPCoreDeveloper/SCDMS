# SCDMS.Aspire.Hosting

.NET Aspire hosting integration for the [SCDMS](https://github.com/MPCoreDeveloper/SCDMS)
web studio — the database studio for [SharpCoreDB](https://github.com/MPCoreDeveloper/SharpCoreDB).

Spin up a **SharpCoreDB server container** and an **SCDMS container** as one Aspire application
(pgweb/pgAdmin-style). All SCDMS ⇄ SharpCoreDB data traffic flows over **gRPC**.

## Getting started

```csharp
using Scdms.Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// SharpCoreDB network server container (HTTPS gRPC on 5001, HTTPS REST API on 8443).
var db = builder.AddSharpCoreDB("db")
                .WithServerContainer()
                .WithJwtSecret("a-random-secret-of-at-least-32-characters");

// SCDMS web studio container, auto-wired to the server over gRPC.
builder.AddSCDMS("admin", db);

builder.Build().Run();
```

`AddSCDMS` uses the published image `ghcr.io/mpcoredeveloper/scdms:latest` and configures the
container defaults (plain HTTP on 8080, bind `0.0.0.0`, data in `/app/data`). When the optional
SharpCoreDB resource is passed, `WithGrpcReference` forwards the server's gRPC endpoint through
the `SCDMS__DefaultServerHost`/`SCDMS__DefaultServerPort` environment variables and enables
auto-connect.

## TLS & production notes

SCDMS validates the public certificate of the gRPC endpoint. In production terminate TLS at a
reverse proxy holding a publicly trusted certificate (see the Docker Compose sample in the
SCDMS repository, `samples/docker/`). The SharpCoreDB server container always exposes TLS
endpoints and requires a JWT secret (use `WithJwtSecret`).
