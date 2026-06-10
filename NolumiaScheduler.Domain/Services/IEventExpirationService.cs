using NolumiaScheduler.Domain.Aggregates;

namespace NolumiaScheduler.Domain.Services;

/// <summary>
/// Decides when a calendar event's lifetime is over, i.e. when its final occurrence
/// (including moved occurrences and business-day shifted dates) has ended.
/// Time is always passed in by the caller so the domain stays clock-free.
/// </summary>
public interface IEventExpirationService
{
    /// <summary>
    /// The instant the event's final occurrence ends, or null when the event will never
    /// produce an occurrence (e.g. every occurrence of the series is skipped).
    /// </summary>
    DateTimeOffset? GetLastOccurrenceEnd(CalendarEvent calendarEvent, BusinessCalendar? businessCalendar);

    /// <summary>True when the event has no occurrence ending after <paramref name="reference"/>.</summary>
    bool IsExpired(CalendarEvent calendarEvent, BusinessCalendar? businessCalendar, DateTimeOffset reference);
}
