using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.Repositories;
using NolumiaScheduler.Domain.ValueObjects;
using NolumiaScheduler.Infrastructure.Sqlite.Db;

namespace NolumiaScheduler.Infrastructure.Sqlite.Repositories;

/// <summary>SQLite-backed <see cref="IBusinessCalendarRepository"/>.</summary>
public sealed class SqliteBusinessCalendarRepository : IBusinessCalendarRepository
{
    private readonly BusinessCalendarDao _dao;

    public SqliteBusinessCalendarRepository(SqliteConnectionFactory connectionFactory)
    {
        _dao = new BusinessCalendarDao(connectionFactory);
    }

    public BusinessCalendar? FindById(BusinessCalendarId id)
    {
        var row = _dao.FindById(id.Value);
        return row is null ? null : BusinessCalendarRowMapper.ToDomain(row);
    }

    public IReadOnlyList<BusinessCalendar> FindAll()
        => [.. _dao.FindAll().Select(BusinessCalendarRowMapper.ToDomain)];

    public void Save(BusinessCalendar calendar)
        => _dao.Upsert(BusinessCalendarRowMapper.FromDomain(calendar));

    public void Delete(BusinessCalendarId id)
        => _dao.Delete(id.Value);
}
