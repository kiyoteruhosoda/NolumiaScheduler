using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.ValueObjects;
using Visibility = NolumiaScheduler.Domain.ValueObjects.Visibility;

namespace NolumiaScheduler.CoreTests;

[TestClass]
public class CalendarEventSpanTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void SingleEvent_スパンは予定日になる()
    {
        var ev = Single(new DateTimeOffset(2026, 5, 20, 9, 0, 0, TimeSpan.Zero));

        var (start, end) = ev.GetActiveDateSpan();

        Assert.AreEqual(new LocalDateValue(2026, 5, 20), start);
        Assert.AreEqual(new LocalDateValue(2026, 5, 20), end);
    }

    [TestMethod]
    public void RecurringEvent_スパンは開始日から繰り返し終了日まで()
    {
        var ev = WeeklyMonday(new LocalDateValue(2026, 1, 5), new LocalDateValue(2027, 12, 31));

        var (start, end) = ev.GetActiveDateSpan();

        Assert.AreEqual(new LocalDateValue(2026, 1, 5), start);
        Assert.AreEqual(new LocalDateValue(2027, 12, 31), end);
    }

    [TestMethod]
    public void OverlapsPeriod_スパン内の窓はヒットしスパン外は外れる()
    {
        var ev = WeeklyMonday(new LocalDateValue(2026, 1, 5), new LocalDateValue(2026, 3, 31));

        Assert.IsTrue(ev.OverlapsPeriod(new LocalDateValue(2026, 2, 1), new LocalDateValue(2026, 2, 28)));
        // Well outside the span (beyond the 31-day margin) must not match.
        Assert.IsFalse(ev.OverlapsPeriod(new LocalDateValue(2026, 6, 1), new LocalDateValue(2026, 6, 30)));
        Assert.IsFalse(ev.OverlapsPeriod(new LocalDateValue(2025, 1, 1), new LocalDateValue(2025, 1, 31)));
    }

    private static CalendarEvent Single(DateTimeOffset start) =>
        CalendarEvent.CreateSingle(
            new EventId(Guid.NewGuid().ToString()),
            new EventTitle("Single"),
            location: null,
            Visibility.Public,
            eventType: null,
            description: null,
            new TimeZoneId("UTC"),
            new SingleEventSchedule(
                new LocalDateValue(start.Year, start.Month, start.Day),
                new LocalTimeValue(start.Hour, start.Minute, start.Second), 60),
            Now);

    private static CalendarEvent WeeklyMonday(LocalDateValue startDate, LocalDateValue endDate) =>
        CalendarEvent.CreateRecurring(
            new EventId(Guid.NewGuid().ToString()),
            new EventTitle("Weekly"),
            location: null,
            Visibility.Public,
            eventType: null,
            description: null,
            new TimeZoneId("UTC"),
            new RecurringEventSchedule(
                startDate,
                new LocalTimeValue(10, 0, 0),
                30,
                new RecurrenceRule(RecurrenceType.Weekly, 1, endDate, weekly: new WeeklyRule([Weekday.Monday]))),
            Now);
}
