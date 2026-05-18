using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using NolumiaScheduler.Application.Services;
using NolumiaScheduler.Domain.Repositories;
using NolumiaScheduler.Domain.Services;
using NolumiaScheduler.Infrastructure.Json.Repositories;
using NolumiaScheduler.Infrastructure.Json.Seeder;
using NolumiaScheduler.Presentation.Pages;
using NolumiaScheduler.Presentation.Services;
using NolumiaScheduler.Presentation.ViewModels;

namespace NolumiaScheduler.WinUI;

public partial class App : Microsoft.UI.Xaml.Application
{
    private static IServiceProvider? _services;
    public static IServiceProvider Services => _services
        ?? throw new InvalidOperationException("Services not initialized");

    public static Window? MainWindow { get; private set; }

    public App()
    {
        InitializeComponent();
        _services = BuildServices();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new MainWindow();
        MainWindow.AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico"));
        MainWindow.Activate();
        Services.GetRequiredService<IAlarmService>().Start();

#if DEBUG
        var debugWindow = new AlarmDebugWindow(
            Services.GetRequiredService<IAlarmService>(),
            Services.GetRequiredService<CalendarEventApplicationService>(),
            Services.GetRequiredService<IOccurrenceExpander>());
        debugWindow.Activate();
#endif
    }

    private static IServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

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
        services.AddSingleton<IWeekInteractionCompletionService, NavigateWeekInteractionCompletionService>();

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
