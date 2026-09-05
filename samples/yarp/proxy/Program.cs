using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Yarp.ReverseProxy;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;

// ── YARP reverse proxy sample ─────────────────────────────────────────────────────────────
// All-.NET, TLS-terminating reverse proxy in front of:
//   • SCDMS          → http://scdms:8080   (web UI, HTTP/1.1)
//   • SharpCoreDB    → https://sharpcoredb:5001 (gRPC over HTTP/2)
//
// The proxy is fully driven by environment variables (see docker-compose.yml), mirroring the
// samples/docker Caddyfile topology. Unlike Caddy, YARP does not provision certificates
// automatically: mount a PFX that covers the proxy hostname(s) (dev: localhost via
// `dotnet dev-certs https -ep server-certs/server.pfx -p devonly`; prod: your public cert).

static string GetEnv(string name, string fallback) =>
    Environment.GetEnvironmentVariable(name) ?? fallback;

var scdmsDomain   = GetEnv("SCDMS_DOMAIN", "scdms.example.com");
var grpcDomain    = GetEnv("GRPC_DOMAIN", "scdb.example.com");
var httpPort      = int.Parse(GetEnv("YARP_HTTP_PORT", "80"));
var httpsPort     = int.Parse(GetEnv("YARP_HTTPS_PORT", "443"));
var certPath      = GetEnv("YARP_TLS_CERT_PATH", "/certs/server.pfx");
var certPassword  = GetEnv("YARP_TLS_CERT_PASSWORD", "");
// Plain HTTP is intentional on this leg: SCDMS serves HTTP inside its container and TLS is
// terminated by this proxy (see README). Suppress S5332 accordingly.
var scdmsUpstream = GetEnv("SCDMS_UPSTREAM", "http://scdms:8080"); // NOSONAR
var grpcUpstream  = GetEnv("SCDB_UPSTREAM", "https://sharpcoredb:5001");

var builder = WebApplication.CreateBuilder(args);

// Kestrel endpoints: HTTP for redirects + container healthcheck, HTTPS (HTTP/1.1 + HTTP/2 for
// gRPC) with the mounted TLS certificate.
builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.Listen(IPAddress.Any, httpPort);

    kestrel.Listen(IPAddress.Any, httpsPort, listen =>
    {
        listen.Protocols = HttpProtocols.Http1AndHttp2;

        if (!File.Exists(certPath))
        {
            throw new FileNotFoundException(
                $"TLS certificate not found at '{certPath}'. Generate one and mount it, e.g. " +
                "'dotnet dev-certs https -ep server-certs/server.pfx -p devonly' (see README).");
        }

        listen.UseHttps(https => https.ServerCertificate =
            X509CertificateLoader.LoadPkcs12FromFile(certPath, certPassword));
    });
});

// Host-based routing (mirror of samples/docker/caddy/Caddyfile):
//   <SCDMS_DOMAIN>  → SCDMS web UI (plain HTTP upstream)
//   <GRPC_DOMAIN>   → SharpCoreDB gRPC (HTTPS/HTTP2 upstream, internal certificate skipped,
//                      exactly like Caddy's tls_insecure_skip_verify)
var routes = new[]
{
    new RouteConfig
    {
        RouteId = "scdms-ui",
        ClusterId = "scdms-cluster",
        Match = new RouteMatch { Hosts = new[] { scdmsDomain } },
        Transforms = new[]
        {
            new Dictionary<string, string>
            {
                ["RequestHeader"] = "X-Forwarded-Proto",
                ["Set"] = "https"
            }
        }
    },
    new RouteConfig
    {
        RouteId = "scdb-grpc",
        ClusterId = "scdb-grpc-cluster",
        Match = new RouteMatch { Hosts = new[] { grpcDomain } }
    }
};

var clusters = new[]
{
    new ClusterConfig
    {
        ClusterId = "scdms-cluster",
        Destinations = new Dictionary<string, DestinationConfig>
        {
            ["scdms"] = new DestinationConfig { Address = scdmsUpstream }
        }
    },
    new ClusterConfig
    {
        ClusterId = "scdb-grpc-cluster",
        Destinations = new Dictionary<string, DestinationConfig>
        {
            ["sharpcoredb"] = new DestinationConfig { Address = grpcUpstream }
        },
        HttpClient = new HttpClientConfig
        {
            // The SharpCoreDB container uses an internal/self-signed certificate; the public
            // client still validates the proxy's certificate. Dev only (Caddy equivalent:
            // tls_insecure_skip_verify).
            DangerousAcceptAnyServerCertificate = true,
            SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            EnableMultipleHttp2Connections = true
        },
        HttpRequest = new ForwarderRequestConfig
        {
            Version = HttpVersion.Version20,               // gRPC requires HTTP/2 upstream
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
        }
    }
};

builder.Services.AddReverseProxy().LoadFromMemory(routes, clusters);

var app = builder.Build();

// http → https redirect for the browser UI (never for /health).
app.Use(async (context, next) =>
{
    if (!context.Request.IsHttps && context.Request.Path != "/health")
    {
        var host = httpsPort == 443 ? context.Request.Host.Host : $"{context.Request.Host.Host}:{httpsPort}";
        context.Response.Redirect(
            $"https://{host}{context.Request.PathBase}{context.Request.Path}{context.Request.QueryString}",
            permanent: true);
        return;
    }

    await next(context);
});

// Proxy-container health endpoint (used by the Docker HEALTHCHECK).
app.MapGet("/health", () => Results.Text("OK"));

app.MapReverseProxy();

await app.RunAsync();
