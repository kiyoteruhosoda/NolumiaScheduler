namespace NolumiaScheduler.Infrastructure.Diagnostics;

/// <summary>
/// Where diagnostics are written. Kept beside the data directory (rather than in a temp folder)
/// so "send me your logs" is the same folder the user already knows from Settings.
/// </summary>
public static class AppLogPaths
{
    /// <summary>Log directory for the default per-user data location.</summary>
    public static string DefaultLogDirectory => LogDirectoryFor(StorageContext.DefaultDataDirectory);

    /// <summary>Log directory for an explicit data directory (used by <c>--data-dir</c> overrides).</summary>
    public static string LogDirectoryFor(string dataDirectory) => Path.Combine(dataDirectory, "logs");
}
