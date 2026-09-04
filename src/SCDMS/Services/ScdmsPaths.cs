namespace Scdms.Services;

/// <summary>
/// Central resolution of SCDMS per-user data locations.
/// Windows: %LOCALAPPDATA%\SCDMS — Linux/macOS: ~/.local/share/SCDMS.
/// Container deployments can override the root via SCDMS__DataDirectory.
/// </summary>
public static class ScdmsPaths
{
    public const string DirectoryName = "SCDMS";

    private static string? _rootOverride;

    /// <summary>
    /// Applies an explicit data-root override (e.g. from SCDMS__DataDirectory).
    /// Must be called once at startup before any path is consumed.
    /// </summary>
    public static void Initialize(string? dataDirectoryOverride)
    {
        _rootOverride = string.IsNullOrWhiteSpace(dataDirectoryOverride)
            ? null
            : Path.GetFullPath(dataDirectoryOverride);
    }

    /// <summary>Root folder for settings, workspace state, certificates and the update-check cache.</summary>
    public static string RootDirectory
    {
        get
        {
            if (_rootOverride is not null)
            {
                return _rootOverride;
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                DirectoryName);
        }
    }

    /// <summary>Default root for the built-in databases (scdb, contoso, adventureworks).</summary>
    public static string DefaultDataDirectory => Path.Combine(RootDirectory, "Data");

    /// <summary>Folder holding the locally generated localhost TLS certificate.</summary>
    public static string CertificatesDirectory => Path.Combine(RootDirectory, "certs");
}
