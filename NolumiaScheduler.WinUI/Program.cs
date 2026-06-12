using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppLifecycle;

namespace NolumiaScheduler.WinUI;

/// <summary>
/// Custom entry point (XAML-generated Main is disabled via DISABLE_XAML_GENERATED_MAIN)
/// so single-instancing can be decided before the XAML application starts.
/// </summary>
public static class Program
{
    private const string InstanceKey = "NolumiaScheduler.WinUI.Main";

    [STAThread]
    private static void Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        var mainInstance = AppInstance.FindOrRegisterForKey(InstanceKey);
        if (!mainInstance.IsCurrent)
        {
            // Another instance is already running: hand it our activation so it can
            // bring its window to the front, then exit quietly (no error by design).
            RedirectActivationTo(mainInstance);
            return;
        }

        Microsoft.UI.Xaml.Application.Start(static p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
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
        catch
        {
            // Best effort: if the redirect fails the second instance still exits silently.
        }
    }
}
