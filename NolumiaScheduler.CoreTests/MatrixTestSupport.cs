using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.Repositories;
using NolumiaScheduler.Domain.ValueObjects;

namespace NolumiaScheduler.CoreTests;

/// <summary>In-memory event store implementing both the repository and change-notification ports.</summary>
internal sealed class InMemoryCalendarEventRepository : ICalendarEventRepository, ICalendarEventChanges
{
    public event Action? Changed;
    private readonly Dictionary<string, CalendarEvent> _store = [];

    public CalendarEvent? FindById(EventId id) => _store.TryGetValue(id.Value, out var ev) ? ev : null;
    public IReadOnlyList<CalendarEvent> FindAll() => [.. _store.Values];
    public void Save(CalendarEvent ev) { _store[ev.Id.Value] = ev; Changed?.Invoke(); }
    public void Delete(EventId id) { _store.Remove(id.Value); Changed?.Invoke(); }
}

/// <summary>
/// Builds recurrence rules whose occurrences land on a fixed, predictable set of dates so the
/// matrix can assert concrete results. Anchored to 2026 where 2026-06-01 and 2026-06-15 are both
/// Mondays, so a holiday on 2026-06-15 lets every recurrence type share the same shift assertions.
/// </summary>
internal static class Rec
{
    public static readonly LocalDateValue FarEnd = new(2030, 12, 31);

    public static readonly LocalTimeValue StartTime = new(9, 0, 0);
    public static readonly LocalTimeValue EndTime = new(10, 0, 0);

    // Date shared by a Weekly (Mon), Monthly (15th) and Yearly (Jun 15) occurrence.
    public static readonly LocalDateValue SharedHolidayDate = new(2026, 6, 15);

    public static LocalDateValue Start(RecurrenceType type) => type switch
    {
        RecurrenceType.Weekly => new LocalDateValue(2026, 6, 1),   // Monday
        RecurrenceType.Monthly => new LocalDateValue(2026, 6, 15),
        RecurrenceType.Yearly => new LocalDateValue(2026, 6, 15),
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    public static RecurrenceRule Rule(RecurrenceType type, LocalDateValue? end = null, AdjustmentRule? adjustment = null)
        => type switch
        {
            RecurrenceType.Weekly => new RecurrenceRule(
                RecurrenceType.Weekly, 1, end ?? FarEnd,
                weekly: new WeeklyRule([Weekday.Monday]), adjustment: adjustment),
            RecurrenceType.Monthly => new RecurrenceRule(
                RecurrenceType.Monthly, 1, end ?? FarEnd,
                monthly: new DayOfMonthMonthlyRule(15), adjustment: adjustment),
            RecurrenceType.Yearly => new RecurrenceRule(
                RecurrenceType.Yearly, 1, end ?? FarEnd,
                yearly: new DayOfMonthYearlyRule(6, 15), adjustment: adjustment),
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };

    /// <summary>The first <paramref name="count"/> occurrence dates with no adjustment applied.</summary>
    public static LocalDateValue[] Expected(RecurrenceType type, int count)
    {
        var start = Start(type);
        return type switch
        {
            RecurrenceType.Weekly => [.. Enumerable.Range(0, count).Select(i => start.AddDays(7 * i))],
            RecurrenceType.Monthly => [.. Enumerable.Range(0, count).Select(i => start.AddMonths(i))],
            RecurrenceType.Yearly => [.. Enumerable.Range(0, count).Select(i => start.AddYears(i))],
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };
    }

    public static readonly RecurrenceType[] AllTypes =
        [RecurrenceType.Weekly, RecurrenceType.Monthly, RecurrenceType.Yearly];
}
