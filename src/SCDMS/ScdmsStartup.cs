using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using SafeWebCore.Extensions;
using SharpCoreDB;
using Scdms.Models;
using Scdms.Services;

namespace Scdms;

/// <summary>
/// Host (Kestrel), DI and middleware configuration helpers. Kept in a dedicated type so the
/// top-level statements in Program.cs stay within the S3776 cognitive-complexity budget.
/// </summary>
internal static class ScdmsStartup
{
    /// <summary>
    /// Configures the Kestrel listener. Desktop mode serves HTTPS with a locally generated
    /// self-signed localhost certificate; container mode (SCDMS__EnableHttps=false) serves
    /// plain HTTP on <see cref="ScdmsOptions.HttpPort"/> behind a TLS-terminating proxy.
    /// </summary>
    public static void ConfigureKestrel(WebApplicationBuilder builder, ScdmsOptions scdmsOptions)
    {
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

                return;
            }

            if (scdmsOptions.EnableHttps)
            {
                var localhostCertificate = LocalhostCertificateProvider.GetOrCreateCertificate();
                kestrel.ListenLocalhost(scdmsOptions.HttpsPort, listen => listen.UseHttps(localhostCertificate));
            }
            else
            {
                kestrel.ListenLocalhost(scdmsOptions.HttpPort);
            }
        });
    }

    /// <summary>
    /// Registers SCDMS services, session state, DataProtection (keys persisted under the
    /// SCDMS data root so they survive container restarts) and optional forwarded-headers
    /// support for reverse-proxy deployments.
    /// </summary>
    public static void ConfigureServices(WebApplicationBuilder builder, ScdmsOptions scdmsOptions)
    {
        builder.Services.Configure<ScdmsOptions>(builder.Configuration.GetSection(ScdmsOptions.SectionName));
        builder.Services.AddHttpContextAccessor();

        Directory.CreateDirectory(Path.Combine(ScdmsPaths.RootDirectory, "dataprotection"));
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
    }

    /// <summary>
    /// Builds the request pipeline. HSTS and automatic HTTPS redirection only apply when SCDMS
    /// itself serves HTTPS; in container/HTTP mode the reverse proxy terminates TLS.
    /// </summary>
    public static void ConfigurePipeline(WebApplication app, ScdmsOptions scdmsOptions)
    {
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
    }
}
