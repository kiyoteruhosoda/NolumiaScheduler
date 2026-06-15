using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.ValueObjects;

namespace NolumiaScheduler.Domain.Repositories;

public interface ICalendarEventRepository
{
    CalendarEvent? FindById(EventId id);
    IReadOnlyList<CalendarEvent> FindAll();
    void Save(CalendarEvent calendarEvent);
    void Delete(EventId id);

    /// <summary>
    /// Events that may have an occurrence within the inclusive local-date window
    /// [<paramref name="from"/>, <paramref name="to"/>]. This is a coarse pre-filter:
    /// callers still expand occurrences to get the exact set. The default scans all
    /// events; backends that can index the active span (e.g. SQLite) should override
    /// this to avoid loading the whole store.
    /// </summary>
    IReadOnlyList<CalendarEvent> FindByPeriod(LocalDateValue from, LocalDateValue to)
        => [.. FindAll().Where(e => e.OverlapsPeriod(from, to))];
}

