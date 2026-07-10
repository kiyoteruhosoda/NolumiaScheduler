using NolumiaScheduler.Application.Commands;
using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.Repositories;
using NolumiaScheduler.Domain.ValueObjects;

namespace NolumiaScheduler.Application.Services;

public sealed class BusinessCalendarApplicationService(IBusinessCalendarRepository repo)
{
    private readonly IBusinessCalendarRepository _repo = repo;

    public BusinessCalendar? FindById(string id) =>
        _repo.FindById(new BusinessCalendarId(id));

    public IReadOnlyList<BusinessCalendar> FindAll() =>
        _repo.FindAll();

    public string Create(CreateBusinessCalendarCommand cmd)
    {
        var id = new BusinessCalendarId(Guid.NewGuid().ToString());
        var tz = new TimeZoneId(cmd.TimeZone);
        var calendar = new BusinessCalendar(id, cmd.Name, tz, cmd.Workdays,
            shiftOnHolidaysOnly: cmd.ShiftOnHolidaysOnly, isEnabled: cmd.IsEnabled);
        _repo.Save(calendar);
        return id.Value;
    }

    public void Update(UpdateBusinessCalendarCommand cmd)
    {
        var existing = _repo.FindById(new BusinessCalendarId(cmd.CalendarId))
            ?? throw new InvalidOperationException($"Business calendar '{cmd.CalendarId}' not found.");

        existing.Update(cmd.Name, cmd.Workdays, cmd.ShiftOnHolidaysOnly, cmd.IsEnabled);
        _repo.Save(existing);
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
