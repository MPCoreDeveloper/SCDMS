using System.Reflection;

namespace Scdms.Services;

/// <summary>
/// Exposes the running SCDMS version, sourced from the assembly informational version
/// (overridden by the release workflow from the git tag).
/// </summary>
public static class ScdmsVersion
{
    public static string Current { get; } =
        (typeof(ScdmsVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? ResolveAssemblyVersion())
        .Split('+')[0];

    /// <summary>
    /// Fallback when no informational version attribute is present (e.g. local development
    /// builds). Uses the built assembly version (1.0.0.0 by default) instead of a hardcoded
    /// literal so the reported value always matches the binary.
    /// </summary>
    private static string ResolveAssemblyVersion() =>
        typeof(ScdmsVersion).Assembly.GetName().Version?.ToString() ?? "unknown";

    /// <summary>Display form, e.g. "v1.0.0".</summary>
    public static string Display => $"v{Current}";
}
