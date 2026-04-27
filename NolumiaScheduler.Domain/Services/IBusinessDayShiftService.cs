using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.ValueObjects;

namespace NolumiaScheduler.Domain.Services
{
    public interface IBusinessDayShiftService
    {
        LocalDateValue Shift(
            LocalDateValue date,
            AdjustmentRule rule,
            BusinessCalendar calendar);
    }
}
