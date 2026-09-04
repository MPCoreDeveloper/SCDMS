namespace Scdms.Models;

/// <summary>
/// Provides configuration options for the SCDMS runtime.
/// Bound from the "SCDMS" configuration section (environment prefix: SCDMS__).
/// </summary>
public sealed class ScdmsOptions
{
    public const string SectionName = "SCDMS";

    public string BindAddress { get; set; } = "localhost";

    public int HttpsPort { get; set; } = 5443;

    public int QueryTimeoutSeconds { get; set; } = 30;

    public int ResultRowLimit { get; set; } = 200;

    public int MaxRecentConnections { get; set; } = 8;

    public int MaxSavedQueries { get; set; } = 50;

    public int MaxQueryHistoryItems { get; set; } = 100;

    /// <summary>
    /// Name of the default local database created on first launch.
    /// </summary>
    public string DefaultDatabaseName { get; set; } = "scdb";

    /// <summary>
    /// Password used for the default local database and built-in sample databases.
    /// </summary>
    // NOSONAR(S2068): intentional well-known default for local-only sample databases,
    // documented in docs/usage.md and surfaced as a UI hint; overridable via appsettings/SCDMS__*.
    public string DefaultDatabasePassword { get; set; } = "scdb";

    /// <summary>
    /// Full path to the default database. When empty, resolves to
    /// %LOCALAPPDATA%\SCDMS\Data\&lt;DefaultDatabaseName&gt;.
    /// </summary>
    public string DefaultDatabasePath { get; set; } = string.Empty;

    /// <summary>
    /// Root directory for built-in sample databases (Contoso, AdventureWorks).
    /// When empty, resolves to %LOCALAPPDATA%\SCDMS\Data.
    /// </summary>
    public string SampleDatabasesDirectory { get; set; } = string.Empty;

    /// <summary>
    /// When true, SCDMS periodically checks GitHub Releases for a newer version (max once per 24h).
    /// </summary>
    public bool UpdateCheckEnabled { get; set; } = true;

    /// <summary>
    /// GitHub repository (owner/name) used for update checks.
    /// </summary>
    public string GitHubRepository { get; set; } = "MPCoreDeveloper/SCDMS";

    /// <summary>
    /// When true (default), SCDMS serves HTTPS with a locally generated self-signed
    /// certificate. Set to false for container deployments where a reverse proxy
    /// terminates TLS and SCDMS serves plain HTTP on <see cref="HttpPort"/>.
    /// </summary>
    public bool EnableHttps { get; set; } = true;

    /// <summary>
    /// Port used when <see cref="EnableHttps"/> is false (container/HTTP mode). Default: 8080.
    /// </summary>
    public int HttpPort { get; set; } = 8080;

    /// <summary>
    /// When true, SCDMS honors X-Forwarded-Proto/X-Forwarded-For headers. Enable only when
    /// SCDMS runs behind a trusted reverse proxy (container deployments with TLS termination).
    /// </summary>
    public bool UseForwardedHeaders { get; set; }

    /// <summary>
    /// Overrides the per-user SCDMS data root (settings, workspace state, certificates,
    /// built-in databases). When empty, resolves to %LOCALAPPDATA%\SCDMS (Windows) or
    /// ~/.local/share/SCDMS (Linux/macOS). In containers mount a volume here.
    /// </summary>
    public string DataDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Host of the SharpCoreDB server to prefill (and optionally auto-connect to) when the
    /// viewer opens. When empty, SCDMS keeps its desktop behavior (local default database).
    /// </summary>
    public string DefaultServerHost { get; set; } = string.Empty;

    /// <summary>
    /// gRPC port of the default server. Use the server's gRPC port (5001) when connecting
    /// directly, or 443 when the gRPC endpoint is fronted by a TLS-terminating reverse proxy.
    /// </summary>
    public int DefaultServerPort { get; set; } = 5001;

    /// <summary>
    /// Database name used for the default server connection.
    /// </summary>
    public string DefaultServerDatabase { get; set; } = "master";

    /// <summary>
    /// Username used for the default server connection.
    /// </summary>
    public string DefaultServerUsername { get; set; } = "anonymous";

    /// <summary>
    /// Password used for the default server connection. Never persisted; supplied via
    /// environment (SCDMS__DefaultServerPassword) or appsettings.
    /// </summary>
    public string DefaultServerPassword { get; set; } = string.Empty;

    /// <summary>
    /// When true, the default server connection uses TLS (https://). Disable only when the
    /// gRPC endpoint is served over plaintext HTTP inside a trusted network.
    /// </summary>
    public bool DefaultServerUseSsl { get; set; } = true;

    /// <summary>
    /// When true, the gRPC channel prefers HTTP/3 when the server advertises it.
    /// </summary>
    public bool DefaultServerPreferHttp3 { get; set; } = true;

    /// <summary>
    /// When true and <see cref="DefaultServerHost"/> is set, SCDMS connects to the default
    /// server automatically on page load (unless the user disconnected explicitly).
    /// </summary>
    public bool DefaultServerAutoConnect { get; set; }

    /// <summary>
    /// Gets whether a default SharpCoreDB server is configured.
    /// </summary>
    public bool HasDefaultServer => !string.IsNullOrWhiteSpace(DefaultServerHost);
}
