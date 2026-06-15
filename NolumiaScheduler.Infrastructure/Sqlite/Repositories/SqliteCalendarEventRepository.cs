using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.Repositories;
using NolumiaScheduler.Domain.ValueObjects;
using NolumiaScheduler.Infrastructure.Sqlite.Db;

namespace NolumiaScheduler.Infrastructure.Sqlite.Repositories;

/// <summary>
/// SQLite-backed <see cref="ICalendarEventRepository"/>. Mirrors the behaviour of
/// the JSON repository (including <see cref="ICalendarEventChanges"/> notifications)
/// so the two are interchangeable at the composition root.
/// </summary>
public sealed class SqliteCalendarEventRepository : ICalendarEventRepository, ICalendarEventChanges
{
    public event Action? Changed;

    private readonly CalendarEventDao _dao;

    public SqliteCalendarEventRepository(SqliteConnectionFactory connectionFactory)
    {
        _dao = new CalendarEventDao(connectionFactory);
    }

    public CalendarEvent? FindById(EventId id)
    {
        var row = _dao.FindById(id.Value);
        return row is null ? null : CalendarEventRowMapper.ToDomain(row);
    }

    public IReadOnlyList<CalendarEvent> FindAll()
        => [.. _dao.FindAll().Select(CalendarEventRowMapper.ToDomain)];

    public IReadOnlyList<CalendarEvent> FindByPeriod(LocalDateValue from, LocalDateValue to)
        => [.. _dao
            .FindByPeriod(from.ToDateOnly().DayNumber, to.ToDateOnly().DayNumber)
            .Select(CalendarEventRowMapper.ToDomain)];

    public void Save(CalendarEvent calendarEvent)
    {
        _dao.Upsert(CalendarEventRowMapper.FromDomain(calendarEvent));
        Changed?.Invoke();
    }

    public void Delete(EventId id)
    {
        _dao.Delete(id.Value);
        Changed?.Invoke();
    }
}
