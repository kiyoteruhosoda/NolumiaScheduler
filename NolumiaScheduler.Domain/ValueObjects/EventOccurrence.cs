namespace NolumiaScheduler.Domain.ValueObjects;

public sealed class EventOccurrence
{
    public EventId EventId { get; }
    public LocalDateValue Date { get; }
    public LocalTimeValue? StartTime { get; }
    public LocalTimeValue? EndTime { get; }
    public bool AllDay { get; }
    public EventTitle Title { get; }
    public Location? Location { get; }
    public Visibility Visibility { get; }
    public bool IsMoved { get; }
    public bool IsOverridden { get; }

    public EventOccurrence(
        EventId eventId,
        LocalDateValue date,
        LocalTimeValue? startTime,
        LocalTimeValue? endTime,
        bool allDay,
        EventTitle title,
        Location? location,
        Visibility visibility,
        bool isMoved = false,
        bool isOverridden = false)
    {
        EventId = eventId;
        Date = date;
        StartTime = startTime;
        EndTime = endTime;
        AllDay = allDay;
        Title = title;
        Location = location;
        Visibility = visibility;
        IsMoved = isMoved;
        IsOverridden = isOverridden;
    }
}
