using NolumiaScheduler.Presentation.ViewModels;

namespace NolumiaScheduler.Presentation.Services;

public interface IWeekEventLayoutStrategy
{
    IReadOnlyList<WeekEventBlock> Layout(IReadOnlyList<CalendarEventItem> events);
}
