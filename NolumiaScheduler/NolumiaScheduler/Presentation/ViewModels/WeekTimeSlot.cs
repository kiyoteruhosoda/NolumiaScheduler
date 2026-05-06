using NolumiaScheduler.Resources.Strings;

namespace NolumiaScheduler.Presentation.ViewModels;

public sealed class WeekTimeSlot
{
    public WeekTimeSlot(int hour)
    {
        Hour = hour;
        HourLabel = $"{hour:D2}:00";
    }

    public int Hour { get; }
    public string HourLabel { get; }

    public string GetEventText(CalendarEventItem item)
    {
        return item.IsAllDay
            ? $"{AppResources.AllDay} {item.Title}"
            : $"{item.TimeRange} {item.Title}";
    }
}
