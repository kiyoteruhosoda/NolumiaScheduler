using NolumiaScheduler.Domain.ValueObjects;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NolumiaScheduler.Domain.Aggregates;

public class BusinessCalendar(
    BusinessCalendarId id,
    string name,
    TimeZoneId timeZoneId,
    IEnumerable<Weekday> workdays,
    IEnumerable<Holiday>? holidays = null)
{
    public BusinessCalendarId Id { get; } = id ?? throw new ArgumentNullException(nameof(id));
    public string Name { get; private set; } = name ?? throw new ArgumentNullException(nameof(name));
    public TimeZoneId TimeZoneId { get; } = timeZoneId ?? throw new ArgumentNullException(nameof(timeZoneId));
    private readonly HashSet<Weekday> _workdays = [.. workdays];
    private readonly List<Holiday> _holidays = holidays?.ToList() ?? [];

    public IReadOnlyCollection<Weekday> Workdays => _workdays;
    public IReadOnlyList<Holiday> Holidays => _holidays;

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
