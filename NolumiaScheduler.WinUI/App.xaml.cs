using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using NolumiaScheduler.Application.Services;
using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.Repositories;
using NolumiaScheduler.Domain.Services;
using NolumiaScheduler.Domain.ValueObjects;
using NolumiaScheduler.Infrastructure.Json.Repositories;
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
        MainWindow.Activate();
        Services.GetRequiredService<IAlarmService>().Start();
    }

    private static IServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        // Domain services
        services.AddSingleton<IBusinessDayShiftService, BusinessDayShiftService>();
        services.AddSingleton<IOccurrenceExpander, OccurrenceExpander>();

        // Repositories
        services.AddSingleton<ICalendarEventRepository>(_ =>
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NolumiaScheduler", "events");
            var repo = new JsonCalendarEventRepository(dir);
            SeedSampleEvents(repo);
            return repo;
        });

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

    private static void SeedSampleEvents(JsonCalendarEventRepository repo)
    {
        if (repo.FindAll().Count > 0) return;

        var now = DateTimeOffset.UtcNow;

        // Weekly Monday standup 10:00 E0:30 (UTC timezone for seeding)
        var standup = CalendarEvent.CreateRecurring(
            new EventId(Guid.NewGuid().ToString()),
            new EventTitle("Weekly Standup"),
            location: null,
            NolumiaScheduler.Domain.ValueObjects.Visibility.Public,
            eventType: null,
            description: null,
            new TimeZoneId("UTC"),
            allDay: false,
            new RecurringEventSchedule(
                new LocalDateValue(2026, 1, 5),
                new LocalTimeValue(10, 0, 0),
                new LocalTimeValue(10, 30, 0),
                new RecurrenceRule(
                    RecurrenceType.Weekly,
                    1,
                    new LocalDateValue(2027, 12, 31),
                    weekly: new WeeklyRule([Weekday.Monday])),
                allDay: false),
            now);
        repo.Save(standup);

        // All-day event on today's date (UTC)
        var todayUtc = now.UtcDateTime;
        var todayDate = new DateOnly(todayUtc.Year, todayUtc.Month, todayUtc.Day);
        var startOfDay = new DateTimeOffset(todayDate.Year, todayDate.Month, todayDate.Day, 0, 0, 0, TimeSpan.Zero);

        var todayEvent = CalendarEvent.CreateSingle(
            new EventId(Guid.NewGuid().ToString()),
            new EventTitle("Today's Tasks"),
            location: null,
            NolumiaScheduler.Domain.ValueObjects.Visibility.Public,
            eventType: null,
            description: null,
            new TimeZoneId("UTC"),
            allDay: true,
            new SingleEventSchedule(startOfDay, startOfDay.AddDays(1)),
            now);
        repo.Save(todayEvent);

        // Yearly anniversary
        var anniversary = CalendarEvent.CreateRecurring(
            new EventId(Guid.NewGuid().ToString()),
            new EventTitle("Company Anniversary"),
            location: null,
            NolumiaScheduler.Domain.ValueObjects.Visibility.Public,
            eventType: null,
            description: null,
            new TimeZoneId("UTC"),
            allDay: true,
            new RecurringEventSchedule(
                new LocalDateValue(2020, 4, 1),
                startTime: null,
                endTime: null,
                new RecurrenceRule(
                    RecurrenceType.Yearly,
                    1,
                    new LocalDateValue(2030, 12, 31),
                    yearly: new DayOfMonthYearlyRule(4, 1)),
                allDay: true),
            now);
        repo.Save(anniversary);
    }
}
