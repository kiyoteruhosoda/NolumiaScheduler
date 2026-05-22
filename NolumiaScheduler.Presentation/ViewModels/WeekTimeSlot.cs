using NolumiaScheduler.Presentation.Resources.Strings;

namespace NolumiaScheduler.Presentation.ViewModels;

public sealed class WeekTimeSlot(int hour)
{
    public int Hour { get; } = hour;
    public string HourLabel { get; } = $"{hour:D2}:00";

    public static string GetEventText(CalendarEventItem item)
    {
        return item.IsAllDay
            ? $"{AppResources.AllDay} {item.Title}"
            : $"{item.TimeRange} {item.Title}";
    }
}
