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
    IEnumerable<Holiday>? holidays = null,
    bool shiftOnHolidaysOnly = false,
    bool isEnabled = true)
{
    public BusinessCalendarId Id { get; } = id ?? throw new ArgumentNullException(nameof(id));
    public string Name { get; private set; } = name ?? throw new ArgumentNullException(nameof(name));
    public TimeZoneId TimeZoneId { get; } = timeZoneId ?? throw new ArgumentNullException(nameof(timeZoneId));
    private readonly HashSet<Weekday> _workdays = [.. workdays];
    private readonly List<Holiday> _holidays = holidays?.ToList() ?? [];

    public IReadOnlyCollection<Weekday> Workdays => _workdays;
    public IReadOnlyList<Holiday> Holidays => _holidays;

    /// <summary>
    /// When <see langword="true"/>, <see cref="ShiftBusinessDays"/> skips only
    /// holidays; non-working weekdays are treated as valid landing days.
    /// When <see langword="false"/> (default), both holidays and non-workdays are skipped.
    /// </summary>
    public bool ShiftOnHolidaysOnly { get; private set; } = shiftOnHolidaysOnly;

    /// <summary>
    /// When <see langword="false"/>, this calendar is ignored for holiday highlighting
    /// and business-day expansion in the calendar view.
    /// Defaults to <see langword="true"/>.
    /// </summary>
    public bool IsEnabled { get; private set; } = isEnabled;

    public void Update(string name, IEnumerable<Weekday> workdays, bool shiftOnHolidaysOnly = false, bool isEnabled = true)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        _workdays.Clear();
        foreach (var w in workdays)
            _workdays.Add(w);
        ShiftOnHolidaysOnly = shiftOnHolidaysOnly;
        IsEnabled = isEnabled;
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
            var isAcceptable = ShiftOnHolidaysOnly ? !IsHoliday(current) : IsBusinessDay(current);
            if (isAcceptable)
                remaining--;
        }

        return current;
    }
}
