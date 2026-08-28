using System.Reflection;

namespace Scdms.Services;

/// <summary>
/// Exposes the running SCDMS version, sourced from the assembly informational version
/// (overridden by the release workflow from the git tag).
/// </summary>
public static class ScdmsVersion
{
    public static string Current { get; } =
        // NOSONAR(S1313): "1.0.0.0" is the fallback assembly version, not an IP address.
        (typeof(ScdmsVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0.0.0")
        .Split('+')[0];

    /// <summary>Display form, e.g. "v1.0.0".</summary>
    public static string Display => $"v{Current}";
}
