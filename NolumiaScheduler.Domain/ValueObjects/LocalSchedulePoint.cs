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

    // ── UTC instant → local (for the UTC-stored time model, docs/time-model.md §4) ────────────

    /// <summary>The wall-clock date of an instant in the given timezone.</summary>
    public static LocalDateValue LocalDateOf(DateTimeOffset instant, TimeZoneInfo tz)
    {
        var local = TimeZoneInfo.ConvertTime(instant, tz);
        return new LocalDateValue(local.Year, local.Month, local.Day);
    }

    /// <summary>The wall-clock time-of-day of an instant in the given timezone.</summary>
    public static LocalTimeValue LocalTimeOf(DateTimeOffset instant, TimeZoneInfo tz)
    {
        var local = TimeZoneInfo.ConvertTime(instant, tz);
        return new LocalTimeValue(local.Hour, local.Minute, local.Second);
    }

    /// <summary>
    /// Minutes from <paramref name="start"/> to <paramref name="end"/> as times-of-day, wrapping
    /// past midnight when <c>end &lt;= start</c> (<c>end == start</c> means a full 24h). Translates a
    /// legacy/UI start–end pair into a duration.
    /// </summary>
    public static int WrappingDurationMinutes(LocalTimeValue start, LocalTimeValue end)
    {
        var minutes = ((end.Hour * 60) + end.Minute) - ((start.Hour * 60) + start.Minute);
        return minutes > 0 ? minutes : minutes + (24 * 60);
    }
}
