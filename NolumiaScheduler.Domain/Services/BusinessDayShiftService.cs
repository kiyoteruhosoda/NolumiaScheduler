using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.ValueObjects;

namespace NolumiaScheduler.Domain.Services;

public class BusinessDayShiftService : IBusinessDayShiftService
{
    public LocalDateValue Shift(LocalDateValue date, AdjustmentRule rule, BusinessCalendar calendar)
    {
        if (calendar.IsBusinessDay(date))
            return date;

        var direction = rule.Direction == AdjustmentDirection.Forward ? 1 : -1;
        var current = date;

        while (!calendar.IsBusinessDay(current))
        {
            current = current.AddDays(direction);
        }

        return current;
    }
}
