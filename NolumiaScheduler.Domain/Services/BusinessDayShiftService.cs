using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.ValueObjects;

namespace NolumiaScheduler.Domain.Services;

public class BusinessDayShiftService : IBusinessDayShiftService
{
    public LocalDateValue Shift(LocalDateValue date, AdjustmentRule rule, BusinessCalendar calendar)
    {
        if (!rule.Shifts(date, calendar))
            return date;

        if (rule.ShiftAmount == 0)
            return date;

        return rule.ShiftUnit switch
        {
            AdjustmentShiftUnit.BusinessDay => calendar.ShiftBusinessDays(date, rule.ShiftAmount),
            AdjustmentShiftUnit.CalendarDay => date.AddDays(rule.ShiftAmount),
            _ => date,
        };
    }
}
