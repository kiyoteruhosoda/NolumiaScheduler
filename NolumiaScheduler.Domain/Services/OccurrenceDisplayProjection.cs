using NolumiaScheduler.Domain.ValueObjects;

namespace NolumiaScheduler.Domain.Services;

/// <summary>
/// Projects a canonical occurrence (whose <see cref="EventOccurrence.Date"/> /
/// <see cref="EventOccurrence.StartTime"/> are wall-clock in the event's own timezone) into a
/// viewer timezone for display, per docs/time-model.md §4. The returned occurrence's date/time
/// are viewer-local, while its canonical identity is preserved in
/// <see cref="EventOccurrence.SeriesKey"/> so per-occurrence customizations (skip/override/move)
/// and edits still target the right occurrence regardless of the viewer's zone.
/// <para>
/// All-day occurrences (00:00 + 24h) are treated as floating and are NOT shifted: converting a
/// midnight-anchored full day across zones would misplace it, which is not what users expect of
/// an all-day entry.
/// </para>
/// </summary>
public static class OccurrenceDisplayProjection
{
    private const int MinutesPerDay = 24 * 60;

    public static EventOccurrence ToDisplayTimeZone(
        EventOccurrence occurrence, TimeZoneInfo eventTimeZone, TimeZoneInfo viewerTimeZone)
    {
        var canonicalKey = CanonicalKey(occurrence);

        // No projection needed: same zone, or a floating all-day block.
        if (eventTimeZone.Equals(viewerTimeZone) || IsAllDay(occurrence))
            return WithSeriesKey(occurrence, canonicalKey);

        var instant = LocalSchedulePoint.StartInstant(occurrence.Date, occurrence.StartTime, eventTimeZone);
        var viewerLocal = TimeZoneInfo.ConvertTime(instant, viewerTimeZone);

        return new EventOccurrence(
            occurrence.EventId,
            new LocalDateValue(viewerLocal.Year, viewerLocal.Month, viewerLocal.Day),
            new LocalTimeValue(viewerLocal.Hour, viewerLocal.Minute, viewerLocal.Second),
            occurrence.DurationMinutes,
            occurrence.Title,
            occurrence.Location,
            occurrence.Visibility,
            occurrence.IsMoved,
            occurrence.IsOverridden,
            canonicalKey,
            occurrence.ColorKey);
    }

    private static bool IsAllDay(EventOccurrence occ)
        => occ.StartMinuteOfDay == 0 && occ.DurationMinutes == MinutesPerDay;

    // The occurrence's stable identity in the event's own timezone: an existing series key when
    // present (recurring), otherwise the occurrence's own date/time (single).
    private static OccurrenceLocalKey CanonicalKey(EventOccurrence occ)
        => occ.SeriesKey ?? new OccurrenceLocalKey(occ.Date, occ.StartTime);

    private static EventOccurrence WithSeriesKey(EventOccurrence occ, OccurrenceLocalKey key)
        => occ.SeriesKey != null
            ? occ
            : new EventOccurrence(
                occ.EventId, occ.Date, occ.StartTime, occ.DurationMinutes,
                occ.Title, occ.Location, occ.Visibility,
                occ.IsMoved, occ.IsOverridden, key, occ.ColorKey);
}
