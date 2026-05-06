using NolumiaScheduler.Application.Commands;
using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.Repositories;
using NolumiaScheduler.Domain.ValueObjects;

namespace NolumiaScheduler.Application.Services;

public sealed class BusinessCalendarApplicationService
{
    private readonly IBusinessCalendarRepository _repo;

    public BusinessCalendarApplicationService(IBusinessCalendarRepository repo)
    {
        _repo = repo;
    }

    public BusinessCalendar Create(CreateBusinessCalendarCommand cmd)
    {
        var id = new BusinessCalendarId(Guid.NewGuid().ToString());
        var tz = new TimeZoneId(cmd.TimeZone);
        var calendar = new BusinessCalendar(id, cmd.Name, tz, cmd.Workdays);
        _repo.Save(calendar);
        return calendar;
    }

    public void Update(UpdateBusinessCalendarCommand cmd)
    {
        var existing = _repo.FindById(new BusinessCalendarId(cmd.CalendarId))
            ?? throw new InvalidOperationException($"Business calendar '{cmd.CalendarId}' not found.");

        // Reconstitute with updated name and workdays, preserving holidays and timezone
        var updated = new BusinessCalendar(
            existing.Id,
            cmd.Name,
            existing.TimeZoneId,
            cmd.Workdays,
            existing.Holidays);

        _repo.Save(updated);
    }

    public void AddHoliday(AddHolidayCommand cmd)
    {
        var calendar = _repo.FindById(new BusinessCalendarId(cmd.CalendarId))
            ?? throw new InvalidOperationException($"Business calendar '{cmd.CalendarId}' not found.");
        calendar.AddHoliday(new Holiday(cmd.Date, cmd.Name));
        _repo.Save(calendar);
    }

    public void RemoveHoliday(RemoveHolidayCommand cmd)
    {
        var calendar = _repo.FindById(new BusinessCalendarId(cmd.CalendarId))
            ?? throw new InvalidOperationException($"Business calendar '{cmd.CalendarId}' not found.");
        calendar.RemoveHoliday(cmd.Date);
        _repo.Save(calendar);
    }

    public void Delete(DeleteBusinessCalendarCommand cmd)
    {
        _repo.Delete(new BusinessCalendarId(cmd.CalendarId));
    }
}
