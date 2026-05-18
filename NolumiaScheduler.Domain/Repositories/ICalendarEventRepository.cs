using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.ValueObjects;

namespace NolumiaScheduler.Domain.Repositories;

public interface ICalendarEventRepository
{
    CalendarEvent? FindById(EventId id);
    IReadOnlyList<CalendarEvent> FindAll();
    void Save(CalendarEvent calendarEvent);
    void Delete(EventId id);
}
