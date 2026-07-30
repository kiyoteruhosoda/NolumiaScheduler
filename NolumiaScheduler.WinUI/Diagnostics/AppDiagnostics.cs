using System.Globalization;
using NolumiaScheduler.Infrastructure.Diagnostics;
using NolumiaScheduler.WinUI.Helpers;

namespace NolumiaScheduler.WinUI.Diagnostics;

/// <summary>
/// Composition root for diagnostics. Kept separate from the DI container because logging and
/// crash handling must be live before anything else runs — including the code that builds the
/// container, which is itself a place the app has crashed before.
/// </summary>
internal static class AppDiagnostics
{
    private static AppSessionMarker? _session;

    /// <summary>Directory holding the rolling log files and the session marker.</summary>
    public static string LogDirectory { get; } = AppLogPaths.DefaultLogDirectory;

    /// <summary>The session marker for this run; null until <see cref="StartSession"/> runs.</summary>
    public static AppSessionMarker? Session => _session;

    /// <summary>
    /// Installs the log sinks. Safe to call before the single-instance decision: it only appends
    /// to the shared log file and does not touch the session marker, which a second (about to
    /// exit) instance must not overwrite while the real instance is running.
    /// </summary>
    public static void InitializeLogging()
    {
        AppLog.Initialize(new CompositeAppLog(
            new FileAppLog(LogDirectory, TimeProvider.System),
            // Warnings and worse also go to the Windows Application log, so a crash sits on the
            // same timeline as the OS's own Kernel-Power and Application Error entries.
            new WindowsEventLogAppLog(AppLogLevel.Warning)));
    }

    /// <summary>
    /// Claims the session marker, reports how the previous run ended, and installs the global
    /// exception handlers. Call once, only from the instance that is actually going to run.
    /// </summary>
    public static AppSessionMarker StartSession()
    {
        var session = new AppSessionMarker(LogDirectory, TimeProvider.System, AppVersion.GitDescribe);
        _session = session;

        AppLog.Current.Info(
            AppLogCategories.Lifecycle,
            string.Format(
                CultureInfo.InvariantCulture,
                "Process started. version={0} build={1} pid={2} os={3} arch={4} runtime={5}",
                AppVersion.GitDescribe,
                AppVersion.BuildTimestampUtc,
                Environment.ProcessId,
                Environment.OSVersion.VersionString,
                System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture,
                Environment.Version));

        ReportPreviousSession(session);
        CrashReporter.Install(session);
        return session;
    }

    /// <summary>
    /// Records that this run is shutting down on purpose, so the next start does not report a
    /// crash.
    /// </summary>
    public static void MarkCleanExit(string reason)
    {
        AppLog.Current.Info(AppLogCategories.Lifecycle, $"Shutting down ({reason}).");
        _session?.MarkCleanExit(reason);
    }

    private static void ReportPreviousSession(AppSessionMarker session)
    {
        if (session.PreviousSession is not { } previous)
        {
            AppLog.Current.Info(
                AppLogCategories.Lifecycle, "No previous session marker found (first run or cleared logs).");
            return;
        }

        if (previous.CleanExit)
        {
            AppLog.Current.Info(
                AppLogCategories.Lifecycle, $"Previous session exited cleanly. {previous.Describe()}");
            return;
        }

        // The headline record for the whole feature: the previous run went away without ever
        // saying goodbye. lastEvent names what the machine was doing at the time — if that reads
        // "suspend" or "resume", the suspicion is confirmed with a timestamp behind it.
        AppLog.Current.Fatal(
            AppLogCategories.Crash,
            "Previous session ended without a clean shutdown — it was killed, or it crashed without " +
            $"raising a managed exception. {previous.Describe()}");
    }
}
