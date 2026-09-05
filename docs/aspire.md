# .NET Aspire integration (issue #10)

> **Status: geïmplementeerd (2026-09-05).** Fase 4 van de container/gRPC-roadmap is afgerond:
> beide SharpCoreDB-prerequisites (server-image + `SharpCoreDB.Aspire.Hosting`) zijn gepubliceerd,
> en dit repo levert nu het `SCDMS.Aspire.Hosting`-pakket, een voorbeeld-AppHost, een
> NuGet-publish-workflow en documentatie.

## Goal

Draai SharpCoreDB server + SCDMS als één Aspire-app, zoals pgweb/pgAdmin naast PostgreSQL:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var sharpCoreDb = builder.AddSharpCoreDB("db")
                         .WithServerContainer();      // SharpCoreDB server container

// SCDMS web studio, gekoppeld aan de server over gRPC. AddSCDMS registreert meteen het
// HTTP-endpoint (containerpoort 8080) en de SCDMS__* container-voorwaarden.
var scdms = builder.AddSCDMS("admin", sharpCoreDb);

builder.Build().Run();
```

Al het SCDMS ⇄ SharpCoreDB-dataverkeer loopt over **gRPC**. Browser-URL in de AppHost:
`scdms.GetEndpoint("http")`.

## Prerequisites (SharpCoreDB-repo) — klaar

1. ✅ **Server-image gepubliceerd** → `ghcr.io/mpcoredeveloper/sharpcoredb-server`
   (`linux/amd64` + `linux/arm64`, getagd op elke `v*`-tag).
2. ✅ **`SharpCoreDB.Aspire.Hosting`-pakket gepubliceerd** → versie `2.0.0.2`
   (dependency: `Aspire.Hosting` 13.5.3, net10.0).

Publieke API die dit repo gebruikt:

| Lid | Betekenis |
|---|---|
| `AddSharpCoreDB(name, imageTag = null, grpcPort = null, httpsApiPort = null)` | Registreert de servercontainer |
| `.WithServerContainer()` | Documentatie-alias voor container-hosting |
| `.WithImageTag(...)` | Image-tag pinnen |
| `.WithJwtSecret(secret)` | Zet `Server__Security__JwtSecretKey` (min. 32 tekens) |
| `SharpCoreDbServerResource.GrpcEndpointName` (= `"grpc"`) | HTTPS-gRPC-endpoint, containerpoort 5001 |
| `SharpCoreDbServerResource.HttpsApiEndpointName` (= `"https"`) | HTTPS REST API, containerpoort 8443 |

Referentie (SharpCoreDB-kant): [`docs/server/ASPIRE_INTEGRATION.md`](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/server/ASPIRE_INTEGRATION.md)

## Implementatie in dit repo (SCDMS)

### `src/SCDMS.Aspire.Hosting` → NuGet: `SCDMS.Aspire.Hosting`

- **`ScdmsResource`** (`SCDMSResource.cs`) — `ContainerResource` + `IResourceWithConnectionString`
  (web-URL als connection string). Endpoint-naam `http`, containerpoort 8080.
- **`ScdmsAspireExtensions`** (`ScdmsAspireExtensions.cs`):
  - `AddSCDMS(builder, name, sharpCoreDb = null, imageTag = null, port = null)` — registreert de
    SCDMS-container op `ghcr.io/mpcoredeveloper/scdms` met de container-voorwaarden
    (`SCDMS__EnableHttps=false`, `SCDMS__BindAddress=0.0.0.0`, `SCDMS__DataDirectory=/app/data`,
    update-check uit). Wordt `sharpCoreDb` meegegeven, dan volgt automatisch `WithGrpcReference`.
  - `WithGrpcReference(scdms, sharpCoreDb)` — koppelt het `grpc`-endpoint van de server via
    `SCDMS__DefaultServerHost` / `SCDMS__DefaultServerPort`, zet `SCDMS__DefaultServerUseSsl=true`
    en `SCDMS__DefaultServerAutoConnect=true`.

### Voorbeeld-AppHost: `examples/Aspire/SCDMS.AppHost`

Volledig draaibaar voorbeeld. Starten:

```bash
dotnet run --project examples/Aspire/SCDMS.AppHost/SCDMS.AppHost.csproj
```

Lees `examples/Aspire/SCDMS.AppHost/README.md` voor de lokale-dev-/TLS-opmerkingen.

### CI/CD

- `ci.yml` bouwt de volledige oplossing (`SCDMS.slnx`, inclusief de nieuwe projecten) op
  ubuntu/windows/macos.
- `nuget-publish.yml` packt `SCDMS.Aspire.Hosting` en publiceert naar NuGet.org bij elke `v*`-tag
  (vereist het `NUGET_API_KEY`-secret).
- De SCDMS-image (`ghcr.io/mpcoredeveloper/scdms`) wordt gepubliceerd door `docker-publish.yml`
  bij een `v*`-tag (deel 1 van het issue).

## Opmerkingen

- De SharpCoreDB-servercontainer spreekt **uitsluitend TLS** en heeft een certificaat nodig
  (`Server__Security__TlsCertificatePath`); SCDMS valideert het certificaat van het gRPC-endpoint.
- **Productie:** beëindig TLS op een publiek vertrouwde reverse proxy (patroon in
  `samples/docker/`), precies zoals de compose-sample.
- **Lokale dev:** de servercontainer heeft nog steeds een (dev-)certificaat nodig; lees de
  server-side certificaatopties in de SharpCoreDB `ASPIRE_INTEGRATION.md`. Voor een volledig
  vertrouwde lokale run zonder extra CA-mounts blijft de proxy-topologie van `samples/docker/`
  de aanbevolen route.
