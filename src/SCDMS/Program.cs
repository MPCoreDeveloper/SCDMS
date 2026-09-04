using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using SafeWebCore.Extensions;
using SharpCoreDB;
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
Directory.CreateDirectory(Path.Combine(ScdmsPaths.RootDirectory, "dataprotection"));
UserDataMigration.MigrateIfNeeded();

// HTTPS without requiring the .NET SDK: bind Kestrel to a locally generated,
// self-signed localhost certificate (see Services/LocalhostCertificateProvider.cs).
// In container deployments (SCDMS__EnableHttps=false) SCDMS serves plain HTTP on
// HttpPort and a reverse proxy terminates TLS, so no certificate is generated.
builder.WebHost.ConfigureKestrel(kestrel =>
{
    if (IPAddress.TryParse(scdmsOptions.BindAddress, out var bindAddress))
    {
        if (scdmsOptions.EnableHttps)
        {
            var localhostCertificate = LocalhostCertificateProvider.GetOrCreateCertificate();
            kestrel.Listen(bindAddress, scdmsOptions.HttpsPort, listen => listen.UseHttps(localhostCertificate));
        }
        else
        {
            kestrel.Listen(bindAddress, scdmsOptions.HttpPort);
        }
    }
    else if (scdmsOptions.EnableHttps)
    {
        var localhostCertificate = LocalhostCertificateProvider.GetOrCreateCertificate();
        kestrel.ListenLocalhost(scdmsOptions.HttpsPort, listen => listen.UseHttps(localhostCertificate));
    }
    else
    {
        kestrel.ListenLocalhost(scdmsOptions.HttpPort);
    }
});

builder.Services.Configure<ScdmsOptions>(builder.Configuration.GetSection(ScdmsOptions.SectionName));
builder.Services.AddHttpContextAccessor();

// Persist DataProtection keys (used to protect session cookies) under the SCDMS data root so
// they survive container restarts when SCDMS__DataDirectory is a mounted volume.
builder.Services.AddDataProtection()
    .SetApplicationName("SCDMS")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(ScdmsPaths.RootDirectory, "dataprotection")));

// Reverse-proxy deployments (container + TLS-terminating proxy) need SCDMS to trust
// X-Forwarded-For/X-Forwarded-Proto so redirects, CSP and scheme checks stay correct.
// Opt in explicitly with SCDMS__UseForwardedHeaders=true; headers are otherwise ignored.
if (scdmsOptions.UseForwardedHeaders)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });
}

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.Name = ".SCDMS.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    // Desktop mode always serves HTTPS (Secure cookie). In HTTP/container mode the cookie
    // follows the request scheme, which becomes https when the reverse proxy is trusted
    // (SCDMS__UseForwardedHeaders=true + X-Forwarded-Proto).
    options.Cookie.SecurePolicy = scdmsOptions.EnableHttps ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.IdleTimeout = TimeSpan.FromMinutes(20);
});

const string CspSelf = "'self'";

builder.Services.AddNetSecureHeadersStrictAPlus(options =>
{
    options.Csp = options.Csp with
    {
        StyleSrc = CspSelf,
        StyleSrcElem = CspSelf,
        ImgSrc = $"{CspSelf} data:",
        FontSrc = CspSelf,
        ConnectSrc = CspSelf,
        // Trusted Types blocks innerHTML/document.write DOM manipulation used by our vanilla JS.
        // Disable both directives; we enforce XSS safety through strict CSP nonce + strict-dynamic instead.
        RequireTrustedTypesFor = string.Empty,
        TrustedTypes = string.Empty
    };
});
builder.Services.AddSharpCoreDB();
builder.Services.AddSingleton<TransactionContextStore>();
builder.Services.AddSingleton<IRecentConnectionsStore, RecentConnectionsStore>();
builder.Services.AddSingleton<IQueryWorkspaceStore, QueryWorkspaceStore>();
builder.Services.AddSingleton<ISampleDatabaseCatalog, SampleDatabaseCatalog>();
builder.Services.AddScoped<IViewerConnectionService, ViewerConnectionService>();
builder.Services.AddScoped<IViewerTransactionService, ViewerTransactionService>();
builder.Services.AddScoped<ViewerSessionServices>();
builder.Services.AddScoped<IMetadataService, MetadataService>();
builder.Services.AddScoped<IViewerQueryService, ViewerQueryService>();
builder.Services.AddHttpClient();
builder.Services.AddTransient<IUpdateCheckService>(services =>
    new GitHubUpdateCheckService(
        services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(GitHubUpdateCheckService)),
        services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ScdmsOptions>>().Value));
builder.Services.AddRazorPages();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

// Reverse-proxy deployments: honor X-Forwarded-Proto/X-Forwarded-For so scheme-sensitive
// middleware (redirects, CSP, HSTS) stays correct when TLS terminates at the proxy.
if (scdmsOptions.UseForwardedHeaders)
{
    app.UseForwardedHeaders();
}

// HSTS and automatic HTTPS redirection only apply when SCDMS itself serves HTTPS.
// In container/HTTP mode the reverse proxy terminates TLS and performs the upgrade.
if (scdmsOptions.EnableHttps && !app.Environment.IsDevelopment())
{
    app.UseHsts();
}

if (scdmsOptions.EnableHttps)
{
    app.UseHttpsRedirection();
}

app.UseNetSecureHeaders();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.MapRazorPages();

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

