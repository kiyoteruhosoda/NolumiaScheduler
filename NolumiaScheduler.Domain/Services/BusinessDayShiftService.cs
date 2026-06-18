using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.ValueObjects;

namespace NolumiaScheduler.Domain.Services;

public class BusinessDayShiftService : IBusinessDayShiftService
{
    public LocalDateValue Shift(LocalDateValue date, AdjustmentRule rule, BusinessCalendar calendar)
    {
        if (!ConditionMatches(date, rule.Condition, calendar))
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

    private static bool ConditionMatches(LocalDateValue date, AdjustmentCondition condition, BusinessCalendar calendar)
    {
        return condition switch
        {
            AdjustmentCondition.Holiday => calendar.IsHoliday(date),
            AdjustmentCondition.Always => true,
            _ => false,
        };
    }
}
