using System.Collections.ObjectModel;

namespace NolumiaScheduler.Presentation.ViewModels;

public sealed class WeekDayColumn
{
    public WeekDayColumn(string header, bool isHoliday, bool isToday = false)
    {
        Header = header;
        IsHoliday = isHoliday;
        IsToday = isToday;
    }

    public string Header { get; }
    public bool IsHoliday { get; }
    public bool IsToday { get; }
    public Color DayBackgroundColor => IsHoliday ? Color.FromArgb("#fff0f0") : (IsToday ? Color.FromArgb("#eef6ff") : Colors.Transparent);
    public FontAttributes HeaderFontAttributes => IsToday ? FontAttributes.Bold : FontAttributes.None;
    public Color HeaderTextColor => IsToday ? Color.FromArgb("#1a73e8") : Color.FromArgb("#5f6368");
    public ObservableCollection<WeekEventBlock> EventBlocks { get; } = [];
    public ObservableCollection<IWeekGuideLine> GuideLines { get; } = [];
}
