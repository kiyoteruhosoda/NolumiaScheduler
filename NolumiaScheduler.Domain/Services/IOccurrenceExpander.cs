using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.ValueObjects;

namespace NolumiaScheduler.Domain.Services
{
    public interface IOccurrenceExpander
    {
        IReadOnlyList<EventOccurrence> Expand(
            CalendarEvent calendarEvent,
            LocalDateValue fromDate,
            LocalDateValue toDate,
            BusinessCalendar? businessCalendar);
    }
}
