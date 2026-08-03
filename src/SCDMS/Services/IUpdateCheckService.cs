using System.Text.Json;
using System.Text.Json.Serialization;
using Scdms.Models;

namespace Scdms.Services;

/// <summary>Result of a GitHub Releases update check.</summary>
public sealed record UpdateCheckResult(
    [property: JsonPropertyName("updateAvailable")] bool UpdateAvailable,
    [property: JsonPropertyName("currentVersion")] string CurrentVersion,
    [property: JsonPropertyName("latestVersion")] string? LatestVersion,
    [property: JsonPropertyName("releaseUrl")] string? ReleaseUrl,
    [property: JsonPropertyName("checkedAtUtc")] DateTimeOffset CheckedAtUtc);

/// <summary>
/// Checks GitHub Releases for a newer SCDMS version. Results are cached on disk
/// for 24 hours to stay well within the unauthenticated GitHub API rate limit.
/// All failures degrade gracefully to "no update available" (offline-friendly).
/// </summary>
public interface IUpdateCheckService
{
    Task<UpdateCheckResult> CheckAsync(bool force = false, CancellationToken cancellationToken = default);
}

public sealed class GitHubUpdateCheckService(HttpClient httpClient, ScdmsOptions options) : IUpdateCheckService
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(24);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient = httpClient;
    private readonly ScdmsOptions _options = options;

    public async Task<UpdateCheckResult> CheckAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        var currentVersion = ScdmsVersion.Current;
        if (!_options.UpdateCheckEnabled)
        {
            return new UpdateCheckResult(false, currentVersion, null, null, DateTimeOffset.UtcNow);
        }

        var cacheFilePath = Path.Combine(ScdmsPaths.RootDirectory, "update-check.json");
        if (!force && TryReadCache(cacheFilePath, out var cached))
        {
            return cached;
        }

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://api.github.com/repos/{_options.GitHubRepository}/releases/latest");
            request.Headers.UserAgent.ParseAdd("SCDMS-UpdateCheck");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new UpdateCheckResult(false, currentVersion, null, null, DateTimeOffset.UtcNow);
            }

            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            var tagName = document.RootElement.TryGetProperty("tag_name", out var tagElement)
                ? tagElement.GetString() ?? string.Empty
                : string.Empty;
            var releaseUrl = document.RootElement.TryGetProperty("html_url", out var urlElement)
                ? urlElement.GetString()
                : null;

            var latestVersion = tagName.TrimStart('v', 'V');
            var updateAvailable =
                TryParseNormalized(latestVersion, out var latest) &&
                TryParseNormalized(currentVersion, out var current) &&
                latest > current;

            var result = new UpdateCheckResult(updateAvailable, currentVersion, latestVersion, releaseUrl, DateTimeOffset.UtcNow);
            WriteCache(cacheFilePath, result);
            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            return new UpdateCheckResult(false, currentVersion, null, null, DateTimeOffset.UtcNow);
        }
    }

    /// <summary>Parses versions with mixed component counts ("1.0" vs "1.0.0.0") by padding to 4 parts.</summary>
    private static bool TryParseNormalized(string value, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        var parts = value.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Any(part => !int.TryParse(part, out _)))
        {
            return false;
        }

        var padded = string.Join('.', parts.Concat(Enumerable.Repeat("0", Math.Max(0, 4 - parts.Length))));
        return Version.TryParse(padded, out version!);
    }

    private static bool TryReadCache(string cacheFilePath, out UpdateCheckResult cached)
    {
        cached = null!;
        try
        {
            if (!File.Exists(cacheFilePath))
            {
                return false;
            }

            var result = JsonSerializer.Deserialize<UpdateCheckResult>(File.ReadAllText(cacheFilePath), JsonOptions);
            if (result is null || DateTimeOffset.UtcNow - result.CheckedAtUtc > CacheLifetime)
            {
                return false;
            }

            cached = result;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void WriteCache(string cacheFilePath, UpdateCheckResult result)
    {
        try
        {
            Directory.CreateDirectory(ScdmsPaths.RootDirectory);
            File.WriteAllText(cacheFilePath, JsonSerializer.Serialize(result, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Cache is a convenience, never a failure.
        }
    }
}
