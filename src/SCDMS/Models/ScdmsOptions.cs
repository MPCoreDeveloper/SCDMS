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
}
