namespace NolumiaScheduler.Presentation.ViewModels;

public interface IWeekEventLayoutStrategy
{
    IReadOnlyList<WeekEventBlock> Layout(IReadOnlyList<CalendarEventItem> events);
}

