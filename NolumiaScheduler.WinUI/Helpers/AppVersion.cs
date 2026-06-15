using System.Reflection;

namespace NolumiaScheduler.WinUI.Helpers;

/// <summary>
/// Exposes the build-time version information stamped into this assembly by the
/// <c>SetGitVersionInfo</c> MSBuild target. The values tie a running binary back to an
/// exact source revision (git describe + commit SHA), which is what makes them useful
/// for support and crash triage.
/// </summary>
public static class AppVersion
{
    private static readonly Assembly Assembly = typeof(AppVersion).Assembly;

    /// <summary>Base semantic version with the commit SHA appended, e.g. <c>1.0.0+1a2b3c4…</c>.</summary>
    public static string Informational { get; } =
        Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";

    /// <summary>
    /// Tag-aware git label, e.g. <c>v1.0.0-3-g1a2b3c4</c> (3 commits past tag v1.0.0),
    /// the short SHA when untagged, or a <c>-dirty</c> suffix when built from
    /// uncommitted changes.
    /// </summary>
    public static string GitDescribe { get; } = Metadata("GitDescribe") ?? "unknown";

    /// <summary>UTC instant the assembly was built, ISO-8601 (<c>yyyy-MM-ddTHH:mm:ssZ</c>).</summary>
    public static string BuildTimestampUtc { get; } = Metadata("BuildTimestampUtc") ?? "unknown";

    /// <summary>Single-line summary suitable for an "About"/settings row.</summary>
    public static string Display { get; } = $"{GitDescribe} ({BuildTimestampUtc})";

    private static string? Metadata(string key)
    {
        foreach (var attribute in Assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (string.Equals(attribute.Key, key, StringComparison.Ordinal)
                && !string.IsNullOrEmpty(attribute.Value))
            {
                return attribute.Value;
            }
        }
        return null;
    }
}
