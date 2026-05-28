namespace NolumiaScheduler.Domain.ValueObjects;

public sealed class EventOccurrence(
    EventId eventId,
    LocalDateValue date,
    LocalTimeValue? startTime,
    LocalTimeValue? endTime,
    bool allDay,
    EventTitle title,
    Location? location,
    Visibility visibility,
    bool isMoved = false,
    bool isOverridden = false,
    OccurrenceLocalKey? seriesKey = null)
{
    public EventId EventId { get; } = eventId;
    public LocalDateValue Date { get; } = date;
    public LocalTimeValue? StartTime { get; } = startTime;
    public LocalTimeValue? EndTime { get; } = endTime;
    public bool AllDay { get; } = allDay;
    public EventTitle Title { get; } = title;
    public Location? Location { get; } = location;
    public Visibility Visibility { get; } = visibility;
    public bool IsMoved { get; } = isMoved;
    public bool IsOverridden { get; } = isOverridden;

    // Original recurrence key (candidate date + scheduled start) this occurrence was
    // expanded from. Lets callers target the correct occurrence when moving/resizing a
    // recurring instance that may already have been moved.
    public OccurrenceLocalKey? SeriesKey { get; } = seriesKey;
}
