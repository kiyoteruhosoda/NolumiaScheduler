using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using NolumiaScheduler.Application.Services;
using NolumiaScheduler.Domain.Repositories;
using NolumiaScheduler.Domain.Services;
using NolumiaScheduler.Infrastructure.Json.Repositories;
using NolumiaScheduler.Infrastructure.Json.Seeder;
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    public App()
    {
        UnhandledException += OnAppUnhandledException;
        InitializeComponent();
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
            MainWindow = new MainWindow();
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

    private void OnTrayExitRequested()
    {
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

        // Repositories
        services.AddSingleton<JsonCalendarEventRepository>(_ =>
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NolumiaScheduler", "events");
            var repo = new JsonCalendarEventRepository(dir);
            JsonEventSeeder.SeedIfEmpty(repo);
            return repo;
        });
        services.AddSingleton<ICalendarEventRepository>(sp => sp.GetRequiredService<JsonCalendarEventRepository>());
        services.AddSingleton<ICalendarEventChanges>(sp => sp.GetRequiredService<JsonCalendarEventRepository>());

        services.AddSingleton<IBusinessCalendarRepository>(_ =>
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NolumiaScheduler", "business-calendars");
            return new JsonBusinessCalendarRepository(dir);
        });

        // Application services
        services.AddSingleton<CalendarEventApplicationService>();
        services.AddSingleton<BusinessCalendarApplicationService>();

        // Alarm
        services.AddSingleton<IAlarmService, AlarmService>();

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
}
