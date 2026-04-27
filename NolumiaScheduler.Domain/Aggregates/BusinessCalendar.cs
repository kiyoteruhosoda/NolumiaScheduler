using NolumiaScheduler.Domain.ValueObjects;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NolumiaScheduler.Domain.Aggregates;

public class BusinessCalendar
{
    public BusinessCalendarId Id { get; }
    public string Name { get; private set; }
    public TimeZoneId TimeZoneId { get; }
    private readonly HashSet<Weekday> _workdays;
    private readonly List<Holiday> _holidays;

    public IReadOnlyCollection<Weekday> Workdays => _workdays;
    public IReadOnlyList<Holiday> Holidays => _holidays;

    public BusinessCalendar(
        BusinessCalendarId id,
        string name,
        TimeZoneId timeZoneId,
        IEnumerable<Weekday> workdays,
        IEnumerable<Holiday>? holidays = null)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        TimeZoneId = timeZoneId ?? throw new ArgumentNullException(nameof(timeZoneId));
        _workdays = new HashSet<Weekday>(workdays);
        _holidays = holidays?.ToList() ?? [];
    }

    public void AddHoliday(Holiday holiday)
    {
        if (_holidays.Any(h => h.Date.Equals(holiday.Date)))
            return;
        _holidays.Add(holiday);
    }

    public void RemoveHoliday(LocalDateValue date)
    {
        _holidays.RemoveAll(h => h.Date.Equals(date));
    }

    public bool IsHoliday(LocalDateValue date)
    {
        return _holidays.Any(h => h.Date.Equals(date));
    }

    public bool IsBusinessDay(LocalDateValue date)
    {
        if (IsHoliday(date)) return false;
        var weekday = date.DayOfWeek.ToWeekday();
        return _workdays.Contains(weekday);
    }

    public LocalDateValue ShiftBusinessDays(LocalDateValue baseDate, int amount)
    {
        var current = baseDate;
        var direction = amount > 0 ? 1 : -1;
        var remaining = Math.Abs(amount);

        while (remaining > 0)
        {
            current = current.AddDays(direction);
            if (IsBusinessDay(current))
                remaining--;
        }

        return current;
    }
}
