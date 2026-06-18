using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.ValueObjects;

namespace NolumiaScheduler.Domain.Services;

public class EventExpirationService(IOccurrenceExpander expander) : IEventExpirationService
{
    // A business-day adjustment can shift an occurrence past the recurrence end date (e.g. a
    // forward shift across a holiday run). Searching this far past the last scheduled date keeps
    // any realistically shifted occurrence inside the window; candidate dates themselves are
    // still bounded by the recurrence rule's end date.
    private const int ShiftSearchAllowanceDays = 366;

    // IsExpired scans forward from the reference date in bounded chunks so a "no end date"
    // series (end date stored as 9999-12-31) never forces an expansion across millennia.
    private const int ScanChunkDays = 366;
    private const int MaxScanChunks = 80;

    private readonly IOccurrenceExpander _expander = expander;

    public bool IsExpired(CalendarEvent calendarEvent, BusinessCalendar? businessCalendar, DateTimeOffset reference)
    {
        if (calendarEvent.IsSingle())
            return SingleEnd(calendarEvent) <= reference;

        var lastDate = GetLastPossibleOccurrenceDate(calendarEvent);
        var timeZone = calendarEvent.TimeZoneId.ToTimeZoneInfo();
        var referenceLocalDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(reference, timeZone).DateTime);

        // Past the last possible day: every occurrence has ended by the start of the next local
        // day, so the series is over without expanding it.
        if (referenceLocalDate > lastDate.ToDateOnly())
            return true;

        // Otherwise the series is expired only when no remaining occurrence (skips considered)
        // ends after the reference. Scan forward a chunk at a time; one day of back-slop covers
        // an occurrence whose local day started just before the reference instant.
        var from = LocalDateValue.FromDateOnly(AddDaysClamped(referenceLocalDate, -1));
        for (var chunk = 0; chunk < MaxScanChunks; chunk++)
        {
            var to = MinDate(LocalDateValue.FromDateOnly(AddDaysClamped(from.ToDateOnly(), ScanChunkDays - 1)), lastDate);

            foreach (var occurrence in _expander.Expand(calendarEvent, from, to, businessCalendar))
            {
                if (GetOccurrenceEnd(occurrence, timeZone) > reference)
                    return false;
            }

            if (to.CompareTo(lastDate) >= 0)
                return true; // scanned the whole remaining series and found nothing upcoming

            from = to.AddDays(1);
        }

        // Undecided within the scan cap (an effectively endless series that yields no
        // occurrences for decades): err on the side of keeping the event.
        return false;
    }

    /// <summary>
    /// Expands the entire series, so callers must not use this for "no end date" sentinels in
    /// hot paths — expiry checks go through <see cref="IsExpired"/>, which scans incrementally.
    /// </summary>
    public DateTimeOffset? GetLastOccurrenceEnd(CalendarEvent calendarEvent, BusinessCalendar? businessCalendar)
    {
        if (calendarEvent.IsSingle())
            return SingleEnd(calendarEvent);

        var schedule = calendarEvent.RecurringSchedule!;
        var lastDate = GetLastPossibleOccurrenceDate(calendarEvent);
        var anchorLocalDate = LocalSchedulePoint.LocalDateOf(
            schedule.AnchorUtc, calendarEvent.TimeZoneId.ToTimeZoneInfo());

        var occurrences = _expander.Expand(calendarEvent, anchorLocalDate, lastDate, businessCalendar);
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

    /// <summary>
    /// Latest local date on which an occurrence can still happen: the rule's end date, pushed
    /// out by moved occurrences and by the business-day shift allowance.
    /// </summary>
    private static LocalDateValue GetLastPossibleOccurrenceDate(CalendarEvent calendarEvent)
    {
        var schedule = calendarEvent.RecurringSchedule!;

        var lastDate = schedule.RecurrenceRule.EndDate;
        foreach (var move in calendarEvent.Moves)
        {
            if (move.NewDate > lastDate)
                lastDate = move.NewDate;
        }

        if (schedule.RecurrenceRule.Adjustment != null)
            lastDate = LocalDateValue.FromDateOnly(AddDaysClamped(lastDate.ToDateOnly(), ShiftSearchAllowanceDays));

        return lastDate;
    }

    private static DateTimeOffset SingleEnd(CalendarEvent calendarEvent)
        => calendarEvent.SingleSchedule!.EndUtc;

    private static DateTimeOffset GetOccurrenceEnd(EventOccurrence occurrence, TimeZoneInfo timeZone)
    {
        // End is start + duration in wall-clock terms; a cross-midnight occurrence ends on a later
        // local day, which the duration naturally accounts for.
        return LocalSchedulePoint.EndInstant(
            occurrence.Date, occurrence.StartTime, occurrence.DurationMinutes, timeZone);
    }

    private static DateOnly AddDaysClamped(DateOnly date, int days)
    {
        var dayNumber = Math.Clamp(
            (long)date.DayNumber + days,
            DateOnly.MinValue.DayNumber,
            DateOnly.MaxValue.DayNumber);
        return DateOnly.FromDayNumber((int)dayNumber);
    }

    private static LocalDateValue MinDate(LocalDateValue a, LocalDateValue b)
        => a.CompareTo(b) <= 0 ? a : b;
}
