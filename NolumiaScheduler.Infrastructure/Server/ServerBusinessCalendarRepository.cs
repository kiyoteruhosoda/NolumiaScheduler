using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.Exceptions;
using NolumiaScheduler.Domain.Repositories;
using NolumiaScheduler.Domain.ValueObjects;

namespace NolumiaScheduler.Infrastructure.Server;

/// <summary>
/// Business days come from the server's day-off layers (decision 7), so there is exactly one,
/// read-only business calendar (<see cref="ServerDaysOffCalendar"/>). Days off are edited on the
/// server's settings page, not in the app.
/// </summary>
public sealed class ServerBusinessCalendarRepository(ServerCalendarStore store) : IBusinessCalendarRepository
{
    public BusinessCalendar? FindById(BusinessCalendarId id)
        => id.Value == ServerDaysOffCalendar.Id ? store.DaysOff : null;

    public IReadOnlyList<BusinessCalendar> FindAll() => [store.DaysOff];

    public void Save(BusinessCalendar calendar)
        => throw new DomainException("Days off are managed on the Scheduler server.");

    public void Delete(BusinessCalendarId id)
        => throw new DomainException("Days off are managed on the Scheduler server.");
}
