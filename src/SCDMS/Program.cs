using System.Net;
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

// One-time migration of legacy SharpCoreDB.WebViewer user data to the SCDMS folder.
UserDataMigration.MigrateIfNeeded();

var builder = WebApplication.CreateBuilder(args);

var scdmsOptions = builder.Configuration.GetSection(ScdmsOptions.SectionName).Get<ScdmsOptions>() ?? new ScdmsOptions();

// HTTPS without requiring the .NET SDK: bind Kestrel to a locally generated,
// self-signed localhost certificate (see Services/LocalhostCertificateProvider.cs).
var localhostCertificate = LocalhostCertificateProvider.GetOrCreateCertificate();
builder.WebHost.ConfigureKestrel(kestrel =>
{
    if (IPAddress.TryParse(scdmsOptions.BindAddress, out var bindAddress))
    {
        kestrel.Listen(bindAddress, scdmsOptions.HttpsPort, listen => listen.UseHttps(localhostCertificate));
    }
    else
    {
        kestrel.ListenLocalhost(scdmsOptions.HttpsPort, listen => listen.UseHttps(localhostCertificate));
    }
});

builder.Services.Configure<ScdmsOptions>(builder.Configuration.GetSection(ScdmsOptions.SectionName));
builder.Services.AddHttpContextAccessor();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.Name = ".SCDMS.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
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
    app.UseHsts();
}

app.UseHttpsRedirection();
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

// IMPORTANT: create the default "scdb" database on first launch.
using (var scope = app.Services.CreateScope())
{
    var sampleCatalog = scope.ServiceProvider.GetRequiredService<ISampleDatabaseCatalog>();
    await sampleCatalog.EnsureDefaultDatabaseAsync().ConfigureAwait(false);
}

var endpointDisplay = IPAddress.TryParse(scdmsOptions.BindAddress, out var parsed) ? parsed.ToString() : scdmsOptions.BindAddress;
Console.WriteLine($"SCDMS {ScdmsVersion.Display} — Sharp Core Database Management System");
Console.WriteLine($"Listening on https://{endpointDisplay}:{scdmsOptions.HttpsPort}");

await app.RunAsync().ConfigureAwait(false);

