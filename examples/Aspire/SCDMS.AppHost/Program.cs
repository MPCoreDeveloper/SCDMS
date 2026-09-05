using System.Security.Cryptography;
using Aspire.Hosting;
using Scdms.Aspire.Hosting;
using SharpCoreDB.Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// JWT secret for the dev server (min. 32 chars). Override with SCDMS_SERVER_JWT_SECRET to keep
// it stable across runs; otherwise a random per-run secret is generated (dev only - JWT tokens
// do not survive restarts).
var jwtSecret = builder.Configuration["SCDMS_SERVER_JWT_SECRET"];
if (string.IsNullOrWhiteSpace(jwtSecret))
{
    jwtSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
}

// SharpCoreDB network server container (HTTPS gRPC on container port 5001, HTTPS REST API on
// container port 8443) from ghcr.io/mpcoredeveloper/sharpcoredb-server. Pin a specific image
// tag with the SCDB_IMAGE_TAG environment variable; defaults to the published "latest" tag.
var sharpCoreDb = builder.AddSharpCoreDB("db")
    .WithServerContainer()
    .WithJwtSecret(jwtSecret);

if (builder.Configuration["SCDB_IMAGE_TAG"] is { Length: > 0 } serverImageTag)
{
    sharpCoreDb = sharpCoreDb.WithImageTag(serverImageTag);
}

// Optional local development certificate. Drop certs/server.pfx next to this project
// (generate with: dotnet dev-certs https -ep certs/server.pfx -p devonly) and the server
// container mounts and uses it. The server only speaks TLS and will not start without a
// certificate. See README.md for the TLS notes.
var certDirectory = Path.Combine(Environment.CurrentDirectory, "certs");
var certFile = Path.Combine(certDirectory, "server.pfx");
if (Directory.Exists(certDirectory) && File.Exists(certFile))
{
    sharpCoreDb
        .WithBindMount(certDirectory, "/app/certs", isReadOnly: true)
        .WithEnvironment("Server__Security__TlsCertificatePath", "/app/certs/server.pfx");
}

// SCDMS web studio container (plain HTTP on container port 8080), linked to the server over gRPC
// through the SCDMS__DefaultServer* environment variables (see ScdmsAspireExtensions).
builder.AddSCDMS("admin", sharpCoreDb);

await builder.Build().RunAsync();

