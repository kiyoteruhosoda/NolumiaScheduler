namespace NolumiaScheduler.Domain.ValueObjects;

/// <summary>
/// Resolves a wall-clock schedule point (local date + time + duration) into absolute instants
/// using a named timezone, per the DTSTART+TZID time model (<c>docs/time-model.md</c>). The end
/// is computed in wall-clock terms (added to the local start, then resolved), so a fixed-duration
/// occurrence keeps its wall-clock length across DST boundaries.
/// </summary>
public static class LocalSchedulePoint
{
    public static DateTime LocalStart(LocalDateValue date, LocalTimeValue time)
        => date.ToDateOnly().ToDateTime(time.ToTimeOnly());

    public static DateTime LocalEnd(LocalDateValue date, LocalTimeValue time, int durationMinutes)
        => LocalStart(date, time).AddMinutes(durationMinutes);

    public static DateTimeOffset StartInstant(LocalDateValue date, LocalTimeValue time, TimeZoneInfo tz)
    {
        var local = LocalStart(date, time);
        return new DateTimeOffset(local, tz.GetUtcOffset(local));
    }

    public static DateTimeOffset EndInstant(LocalDateValue date, LocalTimeValue time, int durationMinutes, TimeZoneInfo tz)
    {
        var local = LocalEnd(date, time, durationMinutes);
        return new DateTimeOffset(local, tz.GetUtcOffset(local));
    }
}
