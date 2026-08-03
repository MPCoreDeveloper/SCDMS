using Scdms.Models;

namespace Scdms.Services;

/// <summary>
/// Implements the terminal update flow behind 'scdms --check-update' and 'scdms --update'.
/// Updating means re-running the platform installer, which is idempotent.
/// </summary>
public static class UpdateCli
{
    public static async Task<int> RunAsync(bool openReleasePage)
    {
        using var httpClient = new HttpClient();
        var service = new GitHubUpdateCheckService(httpClient, new ScdmsOptions());
        var result = await service.CheckAsync(force: true).ConfigureAwait(false);

        if (!result.UpdateAvailable)
        {
            Console.WriteLine($"SCDMS {result.CurrentVersion} is up to date.");
            return 0;
        }

        Console.WriteLine($"New SCDMS version available: v{result.LatestVersion} (current: v{result.CurrentVersion}).");
        if (!string.IsNullOrWhiteSpace(result.ReleaseUrl))
        {
            Console.WriteLine($"Release notes: {result.ReleaseUrl}");
        }

        Console.WriteLine();
        Console.WriteLine("To update, re-run the installer for your platform (it is idempotent):");
        if (OperatingSystem.IsWindows())
        {
            Console.WriteLine("  irm https://raw.githubusercontent.com/MPCoreDeveloper/SCDMS/main/install/install.ps1 | iex");
        }
        else
        {
            Console.WriteLine("  curl -fsSL https://raw.githubusercontent.com/MPCoreDeveloper/SCDMS/main/install/install.sh | bash");
        }

        if (openReleasePage && !string.IsNullOrWhiteSpace(result.ReleaseUrl))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = result.ReleaseUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Opening a browser is best-effort on headless systems.
            }
        }

        return 0;
    }
}
