namespace Scdms.Services;

/// <summary>
/// Central resolution of SCDMS per-user data locations.
/// Windows: %LOCALAPPDATA%\SCDMS — Linux/macOS: ~/.local/share/SCDMS.
/// </summary>
public static class ScdmsPaths
{
    public const string DirectoryName = "SCDMS";

    /// <summary>Root folder for settings, workspace state, certificates and the update-check cache.</summary>
    public static string RootDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        DirectoryName);

    /// <summary>Default root for the built-in databases (scdb, contoso, adventureworks).</summary>
    public static string DefaultDataDirectory { get; } = Path.Combine(RootDirectory, "Data");

    /// <summary>Folder holding the locally generated localhost TLS certificate.</summary>
    public static string CertificatesDirectory { get; } = Path.Combine(RootDirectory, "certs");
}
