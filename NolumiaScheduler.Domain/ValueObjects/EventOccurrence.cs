namespace NolumiaScheduler.Domain.ValueObjects;

/// <summary>
/// A single concrete occurrence of an event on a local date. Held as start wall-clock time +
/// duration (the end is <c>StartTime + DurationMinutes</c> and may fall on a later day). All-day
/// is not a concept here: a full-day occurrence is simply <c>StartTime 00:00 + 1440</c>.
/// </summary>
public sealed class EventOccurrence(
    EventId eventId,
    LocalDateValue date,
    LocalTimeValue startTime,
    int durationMinutes,
    EventTitle title,
    Location? location,
    Visibility visibility,
    bool isMoved = false,
    bool isOverridden = false,
    OccurrenceLocalKey? seriesKey = null,
    EventColorKey colorKey = EventColorKey.Default,
    bool alarmEnabled = true)
{
    public EventId EventId { get; } = eventId;
    public LocalDateValue Date { get; } = date;
    public LocalTimeValue StartTime { get; } = startTime;
    public int DurationMinutes { get; } = durationMinutes;
    public EventTitle Title { get; } = title;
    public Location? Location { get; } = location;
    public Visibility Visibility { get; } = visibility;
    public bool IsMoved { get; } = isMoved;
    public bool IsOverridden { get; } = isOverridden;
    public EventColorKey ColorKey { get; } = colorKey;

    /// <summary>
    /// Whether this occurrence raises its series alarm. A per-occurrence override can silence a
    /// single occurrence (docs/time-model.md); the series alarm settings still apply otherwise.
    /// </summary>
    public bool AlarmEnabled { get; } = alarmEnabled;

    // Original recurrence key (candidate date + scheduled start) this occurrence was
    // expanded from. Lets callers target the correct occurrence when moving/resizing a
    // recurring instance that may already have been moved.
    public OccurrenceLocalKey? SeriesKey { get; } = seriesKey;

    /// <summary>Minute-of-day of the start (0..1439).</summary>
    public int StartMinuteOfDay => (StartTime.Hour * 60) + StartTime.Minute;

    /// <summary>Exclusive end minute measured from the start day's midnight (may exceed 1440).</summary>
    public int EndMinuteFromStartDay => StartMinuteOfDay + DurationMinutes;

    /// <summary>True when the occurrence's end falls on a later local day than its start.</summary>
    public bool CrossesMidnight => EndMinuteFromStartDay > 1440;
}
