using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.ValueObjects;

namespace NolumiaScheduler.Domain.Repositories
{
    public interface ICalendarEventQueryService
    {
        IReadOnlyList<CalendarEvent> FindByDateRange(LocalDateValue from, LocalDateValue to);
    }
}
