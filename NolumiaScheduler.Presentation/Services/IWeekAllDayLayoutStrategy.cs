using NolumiaScheduler.Presentation.ViewModels;

namespace NolumiaScheduler.Presentation.Services;

public interface IWeekAllDayLayoutStrategy
{
    IReadOnlyList<WeekAllDayEventBlock> Layout(IReadOnlyList<CalendarEventItem> events, DateTime weekStartDate);
}
