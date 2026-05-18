using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.Repositories;
using NolumiaScheduler.Domain.ValueObjects;

namespace NolumiaScheduler.Infrastructure.Json.Seeder;

public static class JsonEventSeeder
{
    public static void SeedIfEmpty(ICalendarEventRepository repo)
    {
        if (repo.FindAll().Count > 0) return;

        var now = DateTimeOffset.UtcNow;

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
