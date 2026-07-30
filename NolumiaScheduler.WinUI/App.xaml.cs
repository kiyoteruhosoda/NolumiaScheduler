using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppNotifications;
using NolumiaScheduler.Application.Services;
using NolumiaScheduler.Domain.Repositories;
using NolumiaScheduler.Domain.Services;
using NolumiaScheduler.Infrastructure;
using NolumiaScheduler.Infrastructure.Diagnostics;
using NolumiaScheduler.Infrastructure.Seeding;
using NolumiaScheduler.Presentation.Resources.Strings;
using NolumiaScheduler.Presentation.Services;
using NolumiaScheduler.Presentation.ViewModels;
using NolumiaScheduler.WinUI.Diagnostics;
using NolumiaScheduler.WinUI.Helpers;
using NolumiaScheduler.WinUI.Presentation.Pages;
using NolumiaScheduler.WinUI.Presentation.Services;

namespace NolumiaScheduler.WinUI;

public partial class App : Microsoft.UI.Xaml.Application
{
    private static IServiceProvider? _services;
    public static IServiceProvider Services => _services
        ?? throw new InvalidOperationException("Services not initialized");

    public static Window? MainWindow { get; private set; }
    private TrayIconManager? _trayIcon;
    private AppNotificationManager? _notificationManager;
    private SystemStateWatcher? _systemStateWatcher;
    private AppHealthMonitor? _healthMonitor;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    public App()
    {
        UnhandledException += OnAppUnhandledException;
        InitializeComponent();

        // The Presentation layer resolves "follow system" theming through this delegate so view
        // models never touch Application.Current (which does not exist in unit tests).
        NolumiaScheduler.Presentation.Helpers.ThemeHelper.UseSystemThemeSource(
            () => Current.RequestedTheme == ApplicationTheme.Dark);

        try
        {
            _services = BuildServices();
            AppLog.Current.Info(AppLogCategories.Lifecycle, "Service container built.");
        }
        catch (Exception ex)
        {
            ShowFatalError("BuildServices", ex);
            Exit();
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            // A second launch is redirected here by Program.Main (single instance);
            // respond by restoring/foregrounding the existing window.
            Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().Activated += OnAppInstanceActivated;

            // Register for app (toast) notifications before the alarm service starts so the
            // first alarm can already send a notification.
            try
            {
                _notificationManager = AppNotificationManager.Default;
                _notificationManager.NotificationInvoked += OnAppNotificationInvoked;
                _notificationManager.Register();
            }
            catch (Exception ex)
            {
                AppLog.Current.Warning(
                    AppLogCategories.Lifecycle,
                    "AppNotificationManager.Register failed; toast notifications are unavailable.", ex);
            }

            // Apply persisted language before the window is created so all localized
            // strings in MainWindow's constructor use the correct culture.
            var savedLanguage = Services.GetRequiredService<IAppSettingsRepository>().GetLanguage();
            if (savedLanguage != null)
                AppResources.Culture = new System.Globalization.CultureInfo(savedLanguage);

            MainWindow = new MainWindow();
            Services.GetRequiredService<ThemeService>().Initialize(MainWindow);
            MainWindow.AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico"));
            MainWindow.Activate();
            Services.GetRequiredService<IAlarmService>().Start();

            _trayIcon = new TrayIconManager(MainWindow, "Nolumia Scheduler");
            _trayIcon.ShowRequested += OnTrayShowRequested;
            _trayIcon.ExitRequested += OnTrayExitRequested;

            // The app is resident in the notification area from launch, not only while minimized:
            // the alarm service keeps running with the window open, so the icon is what tells the
            // user it is alive and gives them a way to quit it.
            _trayIcon.Show();

            if (MainWindow is MainWindow mw)
            {
                // Show() is idempotent; this is a retry for the case where the add at launch
                // failed, so hiding the window can never leave the app unreachable.
                mw.MinimizedToTray += (_, _) => _trayIcon.Show();
            }

            StartDiagnosticsWatchers();

            AppLog.Current.Info(AppLogCategories.Lifecycle, "Launch completed; main window is active.");
        }
        catch (Exception ex)
        {
            ShowFatalError("OnLaunched", ex);
            Exit();
        }
    }

    /// <summary>
    /// Starts the watchers that make a silent death diagnosable: machine power/session
    /// transitions and periodic process-health sampling. Both are best effort — the app must
    /// still run if diagnostics cannot start.
    /// </summary>
    private void StartDiagnosticsWatchers()
    {
        var session = AppDiagnostics.Session;
        if (session is null)
            return;

        try
        {
            _systemStateWatcher = new SystemStateWatcher(AppLog.Current, TimeProvider.System);
            _systemStateWatcher.StateChanged += OnSystemStateChanged;

            // The dispatcher queue is captured here, on the UI thread, so the monitor can ping
            // it from its background timer to tell a hung UI apart from a dead process.
            _healthMonitor = new AppHealthMonitor(AppLog.Current, session, DispatcherQueue.GetForCurrentThread());
        }
        catch (Exception ex)
        {
            AppLog.Current.Error(AppLogCategories.Lifecycle, "Could not start the diagnostics watchers.", ex);
        }
    }

    private void OnSystemStateChanged(string eventName)
    {
        // Windows is shutting down or logging off, so the process is about to be terminated
        // without ever reaching the tray-exit path. Record that as intentional: reporting every
        // PC shutdown as a crash would drown the real ones.
        if (eventName == "endsession")
        {
            AppDiagnostics.MarkCleanExit("windows session ending");
            return;
        }

        // Stamp the transition into the session marker: if the process dies next, the next start
        // reports which machine state it died in — the link between "it crashed" and "we had just
        // resumed" that is otherwise pure guesswork.
        AppDiagnostics.Session?.RecordEvent(eventName);

        // Take a resource sample on both sides of a sleep, since resume is when the graphics
        // stack is re-created and where leaked or lost resources surface.
        if (eventName is "suspend" or "resume" or "resume-automatic" or "resume-critical")
            _healthMonitor?.SampleNow(eventName);
    }

    private void OnAppUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        ShowFatalError("Application.UnhandledException", e.Exception);
        Exit();
    }

    private static void ShowFatalError(string origin, Exception ex)
    {
        // Route through the log sinks first: this is the only path that also reaches the Windows
        // Application event log, and marking the session as crashed means the cause survives even
        // if writing the crash file below fails.
        CrashReporter.ReportFatal(origin, ex);

        var logPath = Path.Combine(StorageContext.DefaultDataDirectory, "crash.log");

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[{DateTime.Now:O}] {origin}");
        sb.AppendLine(ex.ToString());

        // Walk the inner exception chain logging type + HResult for each level, since
        // XamlParseException from the native XAML runtime often carries a COM HRESULT in
        // its InnerException that ToString() omits or renders as the same generic message.
        var inner = ex.InnerException;
        var depth = 1;
        while (inner != null)
        {
            sb.AppendLine($"[InnerException depth={depth}] {inner.GetType().FullName}: 0x{inner.HResult:X8} — {inner.Message}");
            inner = inner.InnerException;
            depth++;
        }

        // Hint: a XamlParseException with no stack trace at startup often means the
        // compiled XAML index (resources.pri) is missing from the publish directory.
        if (ex is Microsoft.UI.Xaml.Markup.XamlParseException)
        {
            var priPath = Path.Combine(AppContext.BaseDirectory, "resources.pri");
            sb.AppendLine($"[Hint] resources.pri present: {File.Exists(priPath)} ({priPath})");
        }

        try
        {
            Directory.CreateDirectory(StorageContext.DefaultDataDirectory);
            // Append rather than overwrite: a repeating crash used to erase the evidence of the
            // first (and usually most informative) occurrence on every restart.
            File.AppendAllText(logPath, sb.ToString());
        }
        catch (Exception writeFailure)
        {
            // The rolling log already has the exception, so failing to write this file is worth
            // recording but must not stop the message below from reaching the user.
            AppLog.Current.Error(AppLogCategories.Crash, $"Could not write {logPath}.", writeFailure);
        }

        const uint MB_ICONERROR = 0x10;
        MessageBox(
            IntPtr.Zero,
            string.Format(AppResources.StartupErrorMessage,
                ex.GetType().Name, $"{ex.HResult:X8}", ex.Message, logPath),
            AppResources.StartupErrorTitle,
            MB_ICONERROR);
    }

    private void OnTrayShowRequested()
    {
        // Deliberately does not hide the tray icon: the icon is resident, so restoring the window
        // must leave it in place. Removing it here meant the app stopped being reachable from the
        // notification area as soon as it was shown once.
        if (MainWindow is not null)
        {
            MainWindow.AppWindow.Show();
            MainWindow.Activate();
        }
    }

    private void OnAppInstanceActivated(object? sender, Microsoft.Windows.AppLifecycle.AppActivationArguments e)
    {
        // Raised on a background thread; restore the window the same way the tray
        // "Show" action does so a tray-minimized instance also comes back.
        MainWindow?.DispatcherQueue.TryEnqueue(OnTrayShowRequested);
    }

    private void OnAppNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        // NotificationInvoked arrives on a background thread; hop to the UI thread and
        // restore the window the same way the tray "Show" action does.
        MainWindow?.DispatcherQueue.TryEnqueue(OnTrayShowRequested);
    }

    private void OnTrayExitRequested()
    {
        // Do NOT call _notificationManager.Unregister() here: it synchronously tears down the
        // COM activator + registry registration and can block the UI thread for several
        // seconds, which is what made app exit feel slow. Registration is meant to persist
        // across runs (only unregister on uninstall/cleanup), so leave it in place.

        // Mark the exit as intentional before tearing anything down, so a failure during
        // shutdown is not reported as a crash on the next start.
        AppDiagnostics.MarkCleanExit("tray exit");

        Services.GetRequiredService<IAlarmService>().Stop();

        _healthMonitor?.Dispose();
        _healthMonitor = null;
        if (_systemStateWatcher is not null)
        {
            _systemStateWatcher.StateChanged -= OnSystemStateChanged;
            _systemStateWatcher.Dispose();
            _systemStateWatcher = null;
        }

        _trayIcon?.Dispose();
        _trayIcon = null;
        if (MainWindow is MainWindow mw)
        {
            mw.ForceClose();
        }
    }

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        // System clock (injected so wall-clock-dependent view models stay testable)
        services.AddSingleton(TimeProvider.System);

        // Domain services
        services.AddSingleton<IBusinessDayShiftService, BusinessDayShiftService>();
        services.AddSingleton<IOccurrenceExpander, OccurrenceExpander>();
        services.AddSingleton<IEventExpirationService, EventExpirationService>();

        // Repositories. The backend is selected here at the composition root from the
        // storage.json config (default JSON); switch it with the management CLI's
        // `set-backend` command. Data migration between backends is also handled by the CLI.
        var storage = new StorageContext(StorageContext.DefaultDataDirectory);
        var backend = storage.Config.GetBackend();
        // Expose the storage location and the active backend so the UI can show them.
        services.AddSingleton(storage);
        services.AddSingleton(new ActiveStorageBackend(backend));
        RegisterRepositories(services, storage, backend);

        // Application services
        services.AddSingleton<CalendarEventApplicationService>();
        services.AddSingleton<BusinessCalendarApplicationService>();
        services.AddSingleton<PurgeExpiredEventsService>();

        // Alarm
        services.AddSingleton<AlarmApplicationService>();
        services.AddSingleton<IAlarmService, AlarmService>();

        // Theme (no UI yet; preference persisted in settings.json and applied at launch)
        services.AddSingleton<ThemeService>();

        // Presentation services
        services.AddSingleton<IWeekEventLayoutStrategy, DefaultWeekEventLayoutStrategy>();
        services.AddSingleton<IWeekAllDayLayoutStrategy, DefaultWeekAllDayLayoutStrategy>();
        services.AddSingleton<IWeekInteractionMapper, WeekInteractionMapper>();
        services.AddSingleton<IWeekGestureArbitrationService, WeekGestureArbitrationService>();
        services.AddSingleton<IWeekAutoScrollService, WeekAutoScrollService>();
        services.AddSingleton<IWeekDragInteractionService, NoOpWeekDragInteractionService>();

        // ViewModels
        services.AddTransient<CalendarViewModel>();
        services.AddTransient<BusinessCalendarListViewModel>();
        services.AddTransient<BusinessCalendarEditViewModel>();
        services.AddTransient<EventEditViewModel>();

        // Pages
        services.AddTransient<CalendarPage>();
        services.AddTransient<BusinessCalendarListPage>();
        services.AddTransient<BusinessCalendarEditPage>();
        services.AddTransient<EventEditPage>();

        return services.BuildServiceProvider();
    }

    private static void RegisterRepositories(ServiceCollection services, StorageContext storage, StorageBackend backend)
    {
        // Build the calendar event repository once and share the single instance for both
        // its read/write contract and its change-notification contract.
        var eventRepository = storage.CreateCalendarEventRepository(backend);
        DefaultEventSeeder.SeedIfEmpty(eventRepository, TimeProvider.System);

        services.AddSingleton(eventRepository);
        services.AddSingleton((ICalendarEventChanges)eventRepository);
        services.AddSingleton(storage.CreateBusinessCalendarRepository(backend));
        services.AddSingleton(storage.CreateAppSettingsRepository(backend));
    }
}
