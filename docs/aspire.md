# .NET Aspire integration (issue #10)

> **Status: design doc.** Fase 4 van de container/gRPC roadmap. Implementatie vereist eerst
> twee zaken in de **SharpCoreDB**-repo (zie "Prerequisites"), daarna kan dit repo een
> `SCDMS.Aspire.Hosting`-pakket toevoegen.

## Goal

Draai SharpCoreDB server + SCDMS als één Aspire-app, zoals pgweb/pgAdmin naast PostgreSQL:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var sharpCoreDb = builder.AddSharpCoreDB("db")
                         .WithServerContainer();      // SharpCoreDB server container

builder.AddSCDMS("admin")
       .WithGrpcReference(sharpCoreDb)                 // SCDMS container linked via gRPC
       .WithHttpEndpoint(port: 8080, name: "http");

builder.Build().Run();
```

Al het SCDMS ⇄ SharpCoreDB-dataverkeer loopt over **gRPC**.

## Prerequisites (SharpCoreDB repo — volgorde)

1. **Server-image publiceren** naar `ghcr.io/mpcoredeveloper/sharpcoredb-server`
   (de `Dockerfile` in `src/SharpCoreDB.Server/` bestaat al; voeg een
   `docker/build-push-action`-workflow toe op `v*`-tags, `linux/amd64`+`linux/arm64`).
2. **`SharpCoreDB.Aspire.Hosting`-pakket** publiceren met:

```csharp
// SharpCoreDB.Aspire.Hosting / SharpCoreDbServerResource.cs
public sealed class SharpCoreDbServerResource(string name)
    : ContainerResource(name), IResourceWithConnectionString
{
    public string? JwtSecretKey { get; set; }
    // ReferenceExpression voor de gRPC-connection string ("Host=...;Port=...;SSL=true")
}
```

```csharp
public static class SharpCoreDbAspireExtensions
{
    // Container-gebaseerd (gepubliceerde image)
    public static IResourceBuilder<SharpCoreDbServerResource> AddSharpCoreDB(
        this IDistributedApplicationBuilder builder, string name) =>
        builder.AddResource(new SharpCoreDbServerResource(name))
               .WithImage("ghcr.io/mpcoredeveloper/sharpcoredb-server")
               .WithImageTag("latest")
               .WithHttpEndpoint(port: 5001, name: "grpc");

    // Convenience-alias voor het issue-snippet
    public static IResourceBuilder<SharpCoreDbServerResource> WithServerContainer(
        this IResourceBuilder<SharpCoreDbServerResource> resource) => resource;
}
```

## SCDMS-repo implementatie (zodra prerequisites klaar zijn)

### Nieuw project `src/SCDMS.Aspire.Hosting/SCDMS.Aspire.Hosting.csproj`

- `TargetFramework: net10.0`
- PackageReference: `Aspire.Hosting` (zelfde versie als SharpCoreDB.AppHost gebruikt,
  i.c. 13.x) + project/package-ref naar `SharpCoreDB.Aspire.Hosting`.
- `SCDMSResource : ContainerResource, IResourceWithConnectionString` (web-URL als
  connection string).

### `SCDMSAspireExtensions.cs` (API-schets)

```csharp
public static class ScdmsAspireExtensions
{
    public static IResourceBuilder<SCDMSResource> AddSCDMS(
        this IDistributedApplicationBuilder builder,
        string name,
        IResourceBuilder<SharpCoreDbServerResource>? sharpCoreDb = null)
    {
        var scdms = builder.AddResource(new SCDMSResource(name))
            .WithImage("ghcr.io/mpcoredeveloper/scdms")
            .WithImageTag("latest")
            .WithHttpEndpoint(targetPort: 8080, name: "http")
            .WithEnvironment("SCDMS__EnableHttps", "false")
            .WithEnvironment("SCDMS__BindAddress", "0.0.0.0")
            .WithEnvironment("SCDMS__DataDirectory", "/app/data")
            .WithEnvironment("SCDMS__DefaultServerAutoConnect", "true");

        return sharpCoreDb is null ? scdms : scdms.WithGrpcReference(sharpCoreDb);
    }

    // Koppelt de gRPC-server aan SCDMS via SCDMS__DefaultServer* omgevingsvariabelen.
    public static IResourceBuilder<SCDMSResource> WithGrpcReference(
        this IResourceBuilder<SCDMSResource> scdms,
        IResourceBuilder<SharpCoreDbServerResource> sharpCoreDb)
    {
        var grpcEndpoint = sharpCoreDb.GetEndpoint("grpc");
        return scdms
            .WithEnvironment("SCDMS__DefaultServerHost", grpcEndpoint)
            .WithEnvironment("SCDMS__DefaultServerPort", grpcEndpoint.Property(EndpointProperty.Port))
            .WithEnvironment("SCDMS__DefaultServerUseSsl", "true")
            .WithEnvironment("SCDMS__DefaultServerAutoConnect", "true");
    }
}
```

### Voorbeeld-AppHost (nieuw project, bijv. `examples/Aspire/SCDMS.AppHost`)

- `<ProjectReference Include="..\..\..\src\SCDMS.Aspire.Hosting" />` +
  `SharpCoreDB.Aspire.Hosting` (NuGet).
- `Program.cs` met het snippet bovenaan dit document; browser-URL via
  `scdms.GetEndpoint("http")`.

### CI

- Bouw/pack `SCDMS.Aspire.Hosting` en publiceer naar NuGet.org bij releases.

## Opmerkingen

- De Aspire-local-run kan de server als container draaien (`WithServerContainer`). Voor
  TLS: in de Aspire-dev-omgeving is een publiek certificaat niet beschikbaar — gebruik de
  publiek-vertrouwde-proxy-aanpak in productie (zie `samples/docker/`) en voor lokale dev
  een dev-certificaat + `tls_insecure_skip_verify`-achtige optie in de hosting-extensie
  (of rechtstreeks container-intern over het Aspire-netwerk met de server-`/health`-check).
