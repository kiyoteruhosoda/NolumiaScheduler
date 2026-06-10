using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.ValueObjects;

namespace NolumiaScheduler.Domain.Services;

public class EventExpirationService(IOccurrenceExpander expander) : IEventExpirationService
{
    // A business-day adjustment can shift an occurrence past the recurrence end date (e.g. a
    // forward shift across a holiday run). Expanding this far past the last scheduled date keeps
    // any realistically shifted occurrence inside the search window; candidate dates themselves
    // are still bounded by the recurrence rule's end date.
    private const int ShiftSearchAllowanceDays = 366;

    private readonly IOccurrenceExpander _expander = expander;

    public bool IsExpired(CalendarEvent calendarEvent, BusinessCalendar? businessCalendar, DateTimeOffset reference)
    {
        var lastEnd = GetLastOccurrenceEnd(calendarEvent, businessCalendar);
        return lastEnd == null || lastEnd <= reference;
    }

    public DateTimeOffset? GetLastOccurrenceEnd(CalendarEvent calendarEvent, BusinessCalendar? businessCalendar)
    {
        if (calendarEvent.IsSingle())
            return calendarEvent.SingleSchedule!.End;

        var schedule = calendarEvent.RecurringSchedule!;

        // Moves may relocate an occurrence beyond the rule's end date, so the search window must
        // cover the latest moved date as well.
        var lastDate = schedule.RecurrenceRule.EndDate;
        foreach (var move in calendarEvent.Moves)
        {
            if (move.NewDate > lastDate)
                lastDate = move.NewDate;
        }

        if (schedule.RecurrenceRule.Adjustment != null)
            lastDate = lastDate.AddDays(ShiftSearchAllowanceDays);

        var occurrences = _expander.Expand(calendarEvent, schedule.StartDate, lastDate, businessCalendar);
        if (occurrences.Count == 0)
            return null;

        var timeZone = calendarEvent.TimeZoneId.ToTimeZoneInfo();
        DateTimeOffset? lastEnd = null;
        foreach (var occurrence in occurrences)
        {
            var end = GetOccurrenceEnd(occurrence, timeZone);
            if (lastEnd == null || end > lastEnd)
                lastEnd = end;
        }

        return lastEnd;
    }

    private static DateTimeOffset GetOccurrenceEnd(EventOccurrence occurrence, TimeZoneInfo timeZone)
    {
        // All-day occurrences (and any occurrence without an end time) last until the end of
        // their local day.
        var localEnd = occurrence.EndTime != null
            ? occurrence.Date.ToDateOnly().ToDateTime(occurrence.EndTime.ToTimeOnly())
            : occurrence.Date.ToDateOnly().AddDays(1).ToDateTime(TimeOnly.MinValue);

        return new DateTimeOffset(localEnd, timeZone.GetUtcOffset(localEnd));
    }
}
