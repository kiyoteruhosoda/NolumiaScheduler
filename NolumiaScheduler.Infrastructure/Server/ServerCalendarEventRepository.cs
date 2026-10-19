using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.Exceptions;
using NolumiaScheduler.Domain.Repositories;
using NolumiaScheduler.Domain.ValueObjects;

namespace NolumiaScheduler.Infrastructure.Server;

/// <summary>
/// The app's event repository backed by the Scheduler server (decision 7). Reads come from the local
/// copy (<see cref="ServerCalendarStore"/>); writes go to the server and fail when it cannot be reached.
/// The server's imported occurrences (Outlook, iCalendar) are listed as read-only events so the
/// calendar shows them and their reminders ring.
/// </summary>
public sealed class ServerCalendarEventRepository : ICalendarEventRepository, ICalendarEventChanges
{
    private readonly ServerCalendarStore _store;

    public ServerCalendarEventRepository(ServerCalendarStore store)
    {
        _store = store;
        _store.Changed += () => Changed?.Invoke();
    }

    public event Action? Changed;

    public CalendarEvent? FindById(EventId id)
        => _store.FindEvent(id.Value) ?? _store.ImportedEvents.FirstOrDefault(e => e.Id.Value == id.Value);

    public IReadOnlyList<CalendarEvent> FindAll() => [.. _store.Events, .. _store.ImportedEvents];

    public void Save(CalendarEvent calendarEvent)
    {
        if (calendarEvent.IsReadOnly)
        {
            // Imported occurrences are never written to the server; the app may only toggle their
            // alarm, which lasts until the next refresh.
            _store.PutImported(calendarEvent);
            return;
        }
        _store.Save(calendarEvent);
    }

    public void Delete(EventId id)
    {
        if (_store.ImportedEvents.Any(e => e.Id.Value == id.Value))
            throw new DomainException("Imported events are read-only.");
        _store.Delete(id.Value);
    }
}
