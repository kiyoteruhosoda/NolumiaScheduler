using System.Collections.ObjectModel;

namespace NolumiaScheduler.Presentation.ViewModels;

public sealed class WeekDayColumn
{
    public WeekDayColumn(string header, bool isHoliday)
    {
        Header = header;
        IsHoliday = isHoliday;
    }

    public string Header { get; }
    public bool IsHoliday { get; }
    public Color DayBackgroundColor => IsHoliday ? Color.FromArgb("#fff0f0") : Colors.Transparent;
    public ObservableCollection<WeekEventBlock> EventBlocks { get; } = [];
}
