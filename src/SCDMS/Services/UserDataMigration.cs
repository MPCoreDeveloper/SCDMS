namespace Scdms.Services;

/// <summary>
/// One-time migration of user data from the legacy SharpCoreDB.WebViewer folder
/// to the SCDMS folder. Runs at startup; safe to call on every start.
/// Migrates settings.json, query-workspace.json and the Data folder (databases).
/// </summary>
public static class UserDataMigration
{
    private const string LegacyDirectoryName = "SharpCoreDB.WebViewer";

    /// <summary>
    /// Moves the legacy user-data folder to the SCDMS location when it exists and no
    /// SCDMS folder is present yet. Returns true when a migration was performed.
    /// </summary>
    public static bool MigrateIfNeeded()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(localAppData))
        {
            return false;
        }

        var legacy = Path.Combine(localAppData, LegacyDirectoryName);
        var current = ScdmsPaths.RootDirectory;

        if (!Directory.Exists(legacy) || Directory.Exists(current))
        {
            return false;
        }

        try
        {
            // Same parent directory, so this is an atomic rename on the same volume.
            Directory.Move(legacy, current);
            Console.WriteLine($"[SCDMS] Migrated existing user data from '{legacy}' to '{current}'.");
            return true;
        }
        catch (Exception ex)
        {
            // Never lose user data: on failure the legacy folder stays untouched.
            Console.WriteLine($"[SCDMS] User data migration failed ({ex.Message}). Legacy data left in place; starting with a fresh profile.");
            return false;
        }
    }
}
