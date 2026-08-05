using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppLifecycle;
using NolumiaScheduler.Infrastructure.Diagnostics;
using NolumiaScheduler.WinUI.Diagnostics;

namespace NolumiaScheduler.WinUI;

/// <summary>
/// Custom entry point (XAML-generated Main is disabled via DISABLE_XAML_GENERATED_MAIN)
/// so single-instancing can be decided before the XAML application starts.
/// </summary>
public static class Program
{
    private const string InstanceKey = "NolumiaScheduler.WinUI.Main";

    [STAThread]
    private static void Main()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        // Task Scheduler starts processes with a non-app current directory by default.
        // Pin it to the executable folder so any relative file access remains stable.
        Environment.CurrentDirectory = AppContext.BaseDirectory;

        // Logging comes up before anything else so a failure in the steps below is still
        // recorded. Only the sinks are started here: the session marker is deliberately left
        // alone until we know this process is the one that will run, otherwise a redirected
        // second instance would overwrite the live instance's marker on its way out and make
        // the running app look like it had just started.
        AppDiagnostics.InitializeLogging();

        var mainInstance = AppInstance.FindOrRegisterForKey(InstanceKey);
        if (!mainInstance.IsCurrent)
        {
            // Another instance is already running: hand it our activation so it can
            // bring its window to the front, then exit quietly (no error by design).
            AppLog.Current.Info(
                AppLogCategories.Lifecycle,
                "Another instance is already running; redirecting activation and exiting.");
            RedirectActivationTo(mainInstance);
            return;
        }

        AppDiagnostics.StartSession();

        try
        {
            Microsoft.UI.Xaml.Application.Start(static p =>
            {
                var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                _ = new App();
            });
        }
        catch (Exception ex)
        {
            // Application.Start owns the message loop, so an exception escaping it means the
            // loop itself died. Without this the process would vanish with nothing written.
            CrashReporter.ReportFatal("Application.Start", ex);
            throw;
        }

        // Reached when the message loop ends normally. A run that never gets here left the
        // marker in its "not a clean exit" state, which is what the next start reports.
        AppDiagnostics.MarkCleanExit("message loop ended");
    }

    private static void RedirectActivationTo(AppInstance mainInstance)
    {
        try
        {
            // RedirectActivationToAsync must not be awaited synchronously on the STA
            // main thread (it can deadlock before a dispatcher exists), so run it on a
            // worker thread and block on a semaphore instead — the pattern from the
            // Windows App SDK single-instancing docs.
            var activationArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
            // Not disposed: the worker may Release after a Wait timeout, and a process
            // exit follows immediately anyway.
            var redirected = new SemaphoreSlim(0, 1);
            _ = Task.Run(async () =>
            {
                try
                {
                    await mainInstance.RedirectActivationToAsync(activationArgs);
                }
                finally
                {
                    redirected.Release();
                }
            });
            redirected.Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            // Best effort: if the redirect fails the second instance still exits silently, but
            // the reason is recorded — a failed redirect looks to the user like a launch that
            // did nothing at all.
            AppLog.Current.Warning(AppLogCategories.Lifecycle, "Activation redirect failed.", ex);
        }
    }
}
