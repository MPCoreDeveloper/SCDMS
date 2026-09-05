using System.Net;
using Scdms;
using Scdms.Models;
using Scdms.Services;

// ── CLI entry points (exit without starting the web host) ─────────────────
if (args.Contains("--version", StringComparer.OrdinalIgnoreCase))
{
    Console.WriteLine($"SCDMS {ScdmsVersion.Current}");
    return;
}

if (args.Contains("--check-update", StringComparer.OrdinalIgnoreCase) ||
    args.Contains("--update", StringComparer.OrdinalIgnoreCase))
{
    await UpdateCli.RunAsync(openReleasePage: args.Contains("--update", StringComparer.OrdinalIgnoreCase))
        .ConfigureAwait(false);
    return;
}

var builder = WebApplication.CreateBuilder(args);

var scdmsOptions = builder.Configuration.GetSection(ScdmsOptions.SectionName).Get<ScdmsOptions>() ?? new ScdmsOptions();

// Container support: apply an optional data-root override (SCDMS__DataDirectory) before any
// store touches disk, then run the one-time migration of legacy SharpCoreDB.WebViewer data.
ScdmsPaths.Initialize(scdmsOptions.DataDirectory);
UserDataMigration.MigrateIfNeeded();

// HTTPS without requiring the .NET SDK: bind Kestrel to a locally generated,
// self-signed localhost certificate (see Services/LocalhostCertificateProvider.cs).
// In container deployments (SCDMS__EnableHttps=false) SCDMS serves plain HTTP on
// HttpPort and a reverse proxy terminates TLS, so no certificate is generated.
// Host, DI and middleware configuration lives in ScdmsStartup so the top-level
// statements stay within the S3776 cognitive-complexity budget.
ScdmsStartup.ConfigureKestrel(builder, scdmsOptions);
ScdmsStartup.ConfigureServices(builder, scdmsOptions);

var app = builder.Build();

ScdmsStartup.ConfigurePipeline(app, scdmsOptions);

// Lightweight update-check endpoint consumed by the layout banner (CSP: connect-src 'self').
app.MapGet("/api/update-check", async (IUpdateCheckService updateCheckService, CancellationToken cancellationToken) =>
{
    var result = await updateCheckService.CheckAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
    return Results.Json(result);
});

// Health endpoint used by container HEALTHCHECKs, orchestration (Docker Compose) and
// .NET Aspire dashboard integration.
app.MapGet("/health", () => Results.Text("OK"));

// IMPORTANT: create the default "scdb" database on first launch — but only when no default
// SharpCoreDB server is configured (container/server mode talks to that server over gRPC and
// has no need for the local scratch database).
if (!scdmsOptions.HasDefaultServer)
{
    using var scope = app.Services.CreateScope();
    var sampleCatalog = scope.ServiceProvider.GetRequiredService<ISampleDatabaseCatalog>();
    await sampleCatalog.EnsureDefaultDatabaseAsync().ConfigureAwait(false);
}

var endpointDisplay = IPAddress.TryParse(scdmsOptions.BindAddress, out var parsed) ? parsed.ToString() : scdmsOptions.BindAddress;
var endpointScheme = scdmsOptions.EnableHttps ? "https" : "http";
var endpointPort = scdmsOptions.EnableHttps ? scdmsOptions.HttpsPort : scdmsOptions.HttpPort;
Console.WriteLine($"SCDMS {ScdmsVersion.Display} — Sharp Core Database Management System");
Console.WriteLine($"Listening on {endpointScheme}://{endpointDisplay}:{endpointPort}");

await app.RunAsync().ConfigureAwait(false);

