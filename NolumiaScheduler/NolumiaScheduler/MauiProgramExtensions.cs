using Microsoft.Extensions.Logging;
using NolumiaScheduler.Application.Services;
using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.Repositories;
using NolumiaScheduler.Domain.Services;
using NolumiaScheduler.Domain.ValueObjects;
using NolumiaScheduler.Infrastructure.Json.Repositories;
using NolumiaScheduler.Presentation.Pages;
using NolumiaScheduler.Presentation.Services;
using NolumiaScheduler.Presentation.ViewModels;
using DomainEventId = NolumiaScheduler.Domain.ValueObjects.EventId;
using DomainVisibility = NolumiaScheduler.Domain.ValueObjects.Visibility;

namespace NolumiaScheduler;

public static class MauiProgramExtensions
{
    public static MauiAppBuilder UseSharedMauiApp(this MauiAppBuilder builder)
    {
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        // Domain services
        builder.Services.AddSingleton<IBusinessDayShiftService, BusinessDayShiftService>();
        builder.Services.AddSingleton<IOccurrenceExpander, OccurrenceExpander>();

        // Repositories
        builder.Services.AddSingleton<ICalendarEventRepository>(_ =>
        {
            var dir = Path.Combine(FileSystem.AppDataDirectory, "events");
            var repo = new JsonCalendarEventRepository(dir);
            SeedSampleEvents(repo);
            return repo;
        });

        builder.Services.AddSingleton<IBusinessCalendarRepository>(_ =>
        {
            var dir = Path.Combine(FileSystem.AppDataDirectory, "business-calendars");
            return new JsonBusinessCalendarRepository(dir);
        });

        // Application services
        builder.Services.AddSingleton<CalendarEventApplicationService>();
        builder.Services.AddSingleton<BusinessCalendarApplicationService>();


        // Presentation services
        builder.Services.AddSingleton<IWeekEventLayoutStrategy, DefaultWeekEventLayoutStrategy>();

        // ViewModels
        builder.Services.AddTransient<CalendarViewModel>();
        builder.Services.AddTransient<BusinessCalendarListViewModel>();
        builder.Services.AddTransient<BusinessCalendarEditViewModel>();
        builder.Services.AddTransient<EventEditViewModel>();

        // Pages
        builder.Services.AddTransient<CalendarPage>();
        builder.Services.AddTransient<BusinessCalendarListPage>();
        builder.Services.AddTransient<BusinessCalendarEditPage>();
        builder.Services.AddTransient<EventEditPage>();

        return builder;
    }
    private static void SeedSampleEvents(ICalendarEventRepository repo)
    {
        if (repo.FindAll().Count > 0)
            return;

        var tz = new TimeZoneId("Asia/Tokyo");
        var now = DateTimeOffset.UtcNow;

        // Weekly Monday standup 10:00–10:30
        var standup = CalendarEvent.CreateRecurring(
            new DomainEventId(Guid.NewGuid().ToString()),
            new EventTitle("週次スタンドアップ"),
            location: null,
            DomainVisibility.Public,
            eventType: null,
            description: null,
            tz,
            allDay: false,
            new RecurringEventSchedule(
                new LocalDateValue(2026, 1, 5),
                new LocalTimeValue(10, 0, 0),
                new LocalTimeValue(10, 30, 0),
                new RecurrenceRule(
                    RecurrenceType.Weekly, 1,
                    new LocalDateValue(2027, 12, 31),
                    weekly: new WeeklyRule([Weekday.Monday])),
                allDay: false),
            now);
        repo.Save(standup);

        // All-day event on today's date
        var todayLocal = TimeZoneInfo.ConvertTime(now, tz.ToTimeZoneInfo());
        var todayDate = new DateOnly(todayLocal.Year, todayLocal.Month, todayLocal.Day);
        var tzOffset = tz.ToTimeZoneInfo().GetUtcOffset(now.DateTime);
        var startOfDay = new DateTimeOffset(todayDate.Year, todayDate.Month, todayDate.Day, 0, 0, 0, tzOffset);

        var todayEvent = CalendarEvent.CreateSingle(
            new DomainEventId(Guid.NewGuid().ToString()),
            new EventTitle("本日のタスク"),
            location: null,
            DomainVisibility.Public,
            eventType: null,
            description: null,
            tz,
            allDay: true,
            new SingleEventSchedule(startOfDay, startOfDay.AddDays(1)),
            now);
        repo.Save(todayEvent);

        // Yearly anniversary on April 1
        var anniversary = CalendarEvent.CreateRecurring(
            new DomainEventId(Guid.NewGuid().ToString()),
            new EventTitle("創立記念日"),
            location: null,
            DomainVisibility.Public,
            eventType: null,
            description: null,
            tz,
            allDay: true,
            new RecurringEventSchedule(
                new LocalDateValue(2020, 4, 1),
                startTime: null,
                endTime: null,
                new RecurrenceRule(
                    RecurrenceType.Yearly, 1,
                    new LocalDateValue(2030, 12, 31),
                    yearly: new DayOfMonthYearlyRule(4, 1)),
                allDay: true),
            now);
        repo.Save(anniversary);
    }
}
