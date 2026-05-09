using System.Collections.ObjectModel;
using System.Linq;

namespace NolumiaScheduler.Presentation.ViewModels;

public sealed class WeekDayColumn
{
    public WeekDayColumn(string header, DateTime date, bool isHoliday, bool isToday = false)
    {
        Header = header;
        Date = date.Date;
        IsHoliday = isHoliday;
        IsToday = isToday;
    }

    public string Header { get; }
    public DateTime Date { get; }
    public bool IsHoliday { get; }
    public bool IsToday { get; }
    public Color DayBackgroundColor
        => IsHoliday && Date.DayOfWeek != DayOfWeek.Saturday
            ? Color.FromArgb("#fff0f0")
            : Colors.Transparent;
    public FontAttributes HeaderFontAttributes => IsToday ? FontAttributes.Bold : FontAttributes.None;
    public Color HeaderBackgroundColor => Colors.Transparent;
    public Color HeaderTextColor => IsToday ? Color.FromArgb("#1a73e8") : Color.FromArgb("#5f6368");
    public ObservableCollection<WeekEventBlock> EventBlocks { get; } = [];
    public ObservableCollection<WeekEventBlock> VisibleEventBlocks { get; } = [];
    public ObservableCollection<IWeekGuideLine> GuideLines { get; } = [];

    public void UpdateVisibleRange(int startMinute, int endMinute, int bufferMinutes = 120)
    {
        var from = Math.Max(0, startMinute - bufferMinutes);
        var to = Math.Min(24 * 60, endMinute + bufferMinutes);
        VisibleEventBlocks.Clear();
        foreach (var block in EventBlocks.Where(e => e.EndMinute >= from && e.StartMinute <= to))
            VisibleEventBlocks.Add(block);
    }
}
