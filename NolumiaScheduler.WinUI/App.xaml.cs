using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppNotifications;
using NolumiaScheduler.Application.Services;
using NolumiaScheduler.Domain.Repositories;
using NolumiaScheduler.Domain.Services;
using NolumiaScheduler.Infrastructure;
using NolumiaScheduler.Infrastructure.Json.Repositories;
using NolumiaScheduler.Infrastructure.Seeding;
using NolumiaScheduler.Infrastructure.Sqlite.Db;
using NolumiaScheduler.Infrastructure.Sqlite.Db.Migrations;
using NolumiaScheduler.Infrastructure.Sqlite.Repositories;
using NolumiaScheduler.Presentation.Resources.Strings;
using NolumiaScheduler.Presentation.Services;
using NolumiaScheduler.Presentation.ViewModels;
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
        }
        catch (Exception ex)
        {
            ShowFatalError(ex);
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
                System.Diagnostics.Debug.WriteLine($"[App] AppNotificationManager.Register failed: {ex.Message}");
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

            if (MainWindow is MainWindow mw)
            {
                mw.MinimizedToTray += (_, _) => _trayIcon.Show();
            }
        }
        catch (Exception ex)
        {
            ShowFatalError(ex);
            Exit();
        }
    }

    private void OnAppUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        ShowFatalError(e.Exception);
        Exit();
    }

    private static void ShowFatalError(Exception ex)
    {
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NolumiaScheduler");
        Directory.CreateDirectory(logDir);
        var logPath = Path.Combine(logDir, "crash.log");
        File.WriteAllText(logPath, $"[{DateTime.Now:O}]{Environment.NewLine}{ex}");

        const uint MB_ICONERROR = 0x10;
        MessageBox(
            IntPtr.Zero,
            $"NolumiaScheduler の起動中にエラーが発生しました。\n\n{ex.Message}\n\nログファイル: {logPath}",
            "NolumiaScheduler - 起動エラー",
            MB_ICONERROR);
    }

    private void OnTrayShowRequested()
    {
        _trayIcon?.Hide();
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
        // restore the window the same way the tray "Show" action does (incl. hiding the
        // tray icon, otherwise it would linger after the window is back).
        MainWindow?.DispatcherQueue.TryEnqueue(OnTrayShowRequested);
    }

    private void OnTrayExitRequested()
    {
        _notificationManager?.Unregister();
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

        // Repositories. The backend is selected here at the composition root; JSON is the
        // default and SQLite is opt-in via the NOLUMIA_STORAGE environment variable.
        // A one-time data copy can be requested with NOLUMIA_MIGRATE (see docs/storage.md).
        RunStorageMigrationIfRequested();

        if (ResolveStorageBackend() == StorageBackend.Sqlite)
            RegisterSqliteRepositories(services);
        else
            RegisterJsonRepositories(services);

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

    /// <summary>
    /// Resolves the persistence backend. Defaults to <see cref="StorageBackend.Json"/>;
    /// set the <c>NOLUMIA_STORAGE</c> environment variable to <c>Sqlite</c> to switch.
    /// </summary>
    private static StorageBackend ResolveStorageBackend()
    {
        var raw = Environment.GetEnvironmentVariable("NOLUMIA_STORAGE");
        return Enum.TryParse<StorageBackend>(raw, ignoreCase: true, out var backend)
            ? backend
            : StorageBackend.Json;
    }

    private static string AppDataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NolumiaScheduler");

    private static string EventsDir => Path.Combine(AppDataDir, "events");
    private static string BusinessCalendarsDir => Path.Combine(AppDataDir, "business-calendars");
    private static string SqliteDbPath => Path.Combine(AppDataDir, "nolumia.db");

    private readonly record struct RepositorySet(
        ICalendarEventRepository Events,
        IBusinessCalendarRepository BusinessCalendars,
        IAppSettingsRepository Settings);

    private static RepositorySet BuildJsonRepositories() => new(
        new JsonCalendarEventRepository(EventsDir),
        new JsonBusinessCalendarRepository(BusinessCalendarsDir),
        new JsonAppSettingsRepository(AppDataDir));

    private static RepositorySet BuildSqliteRepositories()
    {
        var factory = new SqliteConnectionFactory(SqliteDbPath);
        SqliteMigrationRunner.Run(factory);
        return new RepositorySet(
            new SqliteCalendarEventRepository(factory),
            new SqliteBusinessCalendarRepository(factory),
            new SqliteAppSettingsRepository(factory));
    }

    /// <summary>
    /// One-time data copy between backends, triggered by NOLUMIA_MIGRATE
    /// (<c>json-to-sqlite</c> or <c>sqlite-to-json</c>). Skips when the target already
    /// holds data so leaving the variable set cannot clobber newer edits. See
    /// docs/storage.md.
    /// </summary>
    private static void RunStorageMigrationIfRequested()
    {
        var direction = Environment.GetEnvironmentVariable("NOLUMIA_MIGRATE");
        if (string.IsNullOrWhiteSpace(direction)) return;

        var json = BuildJsonRepositories();
        var sqlite = BuildSqliteRepositories();

        var (source, target) = direction.ToLowerInvariant() switch
        {
            "json-to-sqlite" => (json, sqlite),
            "sqlite-to-json" => (sqlite, json),
            _ => throw new InvalidOperationException(
                $"Unknown NOLUMIA_MIGRATE value '{direction}'. Use 'json-to-sqlite' or 'sqlite-to-json'."),
        };

        if (target.Events.FindAll().Count > 0 || target.BusinessCalendars.FindAll().Count > 0)
            return; // already migrated; nothing to do

        var report = StorageMigrator.Migrate(
            source.Events, source.BusinessCalendars, source.Settings,
            target.Events, target.BusinessCalendars, target.Settings);

        System.Diagnostics.Debug.WriteLine(
            $"[Storage] Migrated {report.Events} events and {report.BusinessCalendars} business calendars ({direction}).");
    }

    private static void RegisterJsonRepositories(ServiceCollection services)
    {
        services.AddSingleton<JsonCalendarEventRepository>(sp =>
        {
            var repo = new JsonCalendarEventRepository(EventsDir);
            DefaultEventSeeder.SeedIfEmpty(repo, sp.GetRequiredService<TimeProvider>());
            return repo;
        });
        services.AddSingleton<ICalendarEventRepository>(sp => sp.GetRequiredService<JsonCalendarEventRepository>());
        services.AddSingleton<ICalendarEventChanges>(sp => sp.GetRequiredService<JsonCalendarEventRepository>());

        services.AddSingleton<IAppSettingsRepository>(_ => new JsonAppSettingsRepository(AppDataDir));

        services.AddSingleton<IBusinessCalendarRepository>(_ =>
            new JsonBusinessCalendarRepository(BusinessCalendarsDir));
    }

    private static void RegisterSqliteRepositories(ServiceCollection services)
    {
        var connectionFactory = new SqliteConnectionFactory(SqliteDbPath);
        SqliteMigrationRunner.Run(connectionFactory);
        services.AddSingleton(connectionFactory);

        services.AddSingleton<SqliteCalendarEventRepository>(sp =>
        {
            var repo = new SqliteCalendarEventRepository(sp.GetRequiredService<SqliteConnectionFactory>());
            DefaultEventSeeder.SeedIfEmpty(repo, sp.GetRequiredService<TimeProvider>());
            return repo;
        });
        services.AddSingleton<ICalendarEventRepository>(sp => sp.GetRequiredService<SqliteCalendarEventRepository>());
        services.AddSingleton<ICalendarEventChanges>(sp => sp.GetRequiredService<SqliteCalendarEventRepository>());

        services.AddSingleton<IAppSettingsRepository>(sp =>
            new SqliteAppSettingsRepository(sp.GetRequiredService<SqliteConnectionFactory>()));

        services.AddSingleton<IBusinessCalendarRepository>(sp =>
            new SqliteBusinessCalendarRepository(sp.GetRequiredService<SqliteConnectionFactory>()));
    }
}
