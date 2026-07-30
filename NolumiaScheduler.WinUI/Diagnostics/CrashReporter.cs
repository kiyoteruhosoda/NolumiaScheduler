using NolumiaScheduler.Infrastructure.Diagnostics;

namespace NolumiaScheduler.WinUI.Diagnostics;

/// <summary>
/// Installs the process-wide exception handlers.
/// <para>
/// The XAML <c>Application.UnhandledException</c> event only covers exceptions that reach the
/// UI thread through the XAML dispatcher. Anything thrown on a thread pool thread, a timer
/// callback, or a WinRT completion callback bypasses it entirely and tears the process down
/// with nothing written anywhere — which is exactly the "it was just gone" symptom. These
/// handlers close that gap.
/// </para>
/// </summary>
internal static class CrashReporter
{
    private static AppSessionMarker? _session;
    private static int _installed;

    /// <param name="session">Marker to flag as crashed, so the next start can report the death.</param>
    public static void Install(AppSessionMarker session)
    {
        // Guard against a second call leaving duplicate subscriptions that would double-report.
        if (Interlocked.Exchange(ref _installed, 1) == 1)
            return;

        _session = session;

        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    /// <summary>
    /// Reports a fatal exception the app caught itself (startup failure, XAML unhandled
    /// exception) before it shuts down.
    /// </summary>
    public static void ReportFatal(string origin, Exception exception)
    {
        AppLog.Current.Fatal(AppLogCategories.Crash, $"Fatal exception ({origin}).", exception);
        _session?.MarkCrashed($"{origin}: {exception.GetType().Name}");
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception;
        var description = exception is null
            ? $"non-Exception throw: {e.ExceptionObject}"
            : exception.GetType().Name;

        AppLog.Current.Fatal(
            AppLogCategories.Crash,
            $"Unhandled exception outside the XAML dispatcher (terminating={e.IsTerminating}).",
            exception);

        // Deliberately not suppressed: letting the runtime terminate the process is what
        // produces the ".NET Runtime" / "Application Error" entries in the Windows event log
        // and a Windows Error Reporting dump. Swallowing it here would leave the process in an
        // unknown state and hide the crash from the OS as well.
        if (e.IsTerminating)
            _session?.MarkCrashed($"unhandled-exception: {description}");
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        // Fire-and-forget work (the alarm poll, dispatcher-enqueued async lambdas) faults into
        // here. Since .NET 4.5 this does not crash the process, so without this handler such a
        // failure is completely silent — the app keeps running with a dead subsystem.
        AppLog.Current.Error(
            AppLogCategories.Crash, "Faulted task was never observed.", e.Exception);
        e.SetObserved();
    }

    private static void OnProcessExit(object? sender, EventArgs e)
        // Runs for an orderly exit only; a killed or hard-crashed process never gets here, which
        // is precisely what the session marker is there to detect.
        => AppLog.Current.Info(AppLogCategories.Lifecycle, "Process exiting.");
}
