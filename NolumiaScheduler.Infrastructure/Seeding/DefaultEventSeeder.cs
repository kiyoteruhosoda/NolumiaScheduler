using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.Repositories;
using NolumiaScheduler.Domain.ValueObjects;

namespace NolumiaScheduler.Infrastructure.Seeding;

/// <summary>
/// Seeds a fresh store with sample events. Works against
/// <see cref="ICalendarEventRepository"/> so it is independent of the backend
/// (JSON or SQLite).
/// </summary>
public static class DefaultEventSeeder
{
    public static void SeedIfEmpty(ICalendarEventRepository repo, TimeProvider clock)
    {
        if (repo.FindAll().Count > 0) return;

        var now = clock.GetUtcNow();

        var standup = CalendarEvent.CreateRecurring(
            new EventId(Guid.NewGuid().ToString()),
            new EventTitle("Weekly Standup"),
            location: null,
            NolumiaScheduler.Domain.ValueObjects.Visibility.Public,
            eventType: null,
            description: null,
            new TimeZoneId("UTC"),
            new RecurringEventSchedule(
                new DateTimeOffset(2026, 1, 5, 10, 0, 0, TimeSpan.Zero),
                30,
                new RecurrenceRule(
                    RecurrenceType.Weekly,
                    1,
                    new LocalDateValue(2027, 12, 31),
                    weekly: new WeeklyRule([Weekday.Monday]))),
            now);
        repo.Save(standup);

        var todayUtc = now.UtcDateTime;

        var todayEvent = CalendarEvent.CreateSingle(
            new EventId(Guid.NewGuid().ToString()),
            new EventTitle("Today's Tasks"),
            location: null,
            NolumiaScheduler.Domain.ValueObjects.Visibility.Public,
            eventType: null,
            description: null,
            new TimeZoneId("UTC"),
            // All-day == start at local (here UTC) midnight + a full day (24h).
            new SingleEventSchedule(
                new DateTimeOffset(todayUtc.Year, todayUtc.Month, todayUtc.Day, 0, 0, 0, TimeSpan.Zero), 24 * 60),
            now);
        repo.Save(todayEvent);

        var anniversary = CalendarEvent.CreateRecurring(
            new EventId(Guid.NewGuid().ToString()),
            new EventTitle("Company Anniversary"),
            location: null,
            NolumiaScheduler.Domain.ValueObjects.Visibility.Public,
            eventType: null,
            description: null,
            new TimeZoneId("UTC"),
            new RecurringEventSchedule(
                new DateTimeOffset(2020, 4, 1, 0, 0, 0, TimeSpan.Zero),
                24 * 60,
                new RecurrenceRule(
                    RecurrenceType.Yearly,
                    1,
                    new LocalDateValue(2030, 12, 31),
                    yearly: new DayOfMonthYearlyRule(4, 1))),
            now);
        repo.Save(anniversary);
    }
}
