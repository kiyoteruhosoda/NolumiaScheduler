using System.Collections.ObjectModel;
using NolumiaScheduler.WinUI.Helpers;
using Windows.UI;

namespace NolumiaScheduler.Presentation.ViewModels;

public sealed class WeekDayColumn
{
    public WeekDayColumn(string header, DateTime date, bool isHoliday, bool isToday = false)
    {
        Header = header;
        Date = date.Date;
        IsHoliday = isHoliday;
        IsToday = isToday;
        EventBlocks = [];
        EventBlocks.CollectionChanged += (_, _) => UpdateVisibleRange(_lastStartMinute, _lastEndMinute);
    }

    public string Header { get; }
    public DateTime Date { get; }
    public bool IsHoliday { get; }
    public bool IsToday { get; }

    public Color DayBackgroundColor
    {
        get
        {
            var isDark = ThemeHelper.IsDark;
            var dow = Date.DayOfWeek;
            if (IsHoliday && dow != DayOfWeek.Saturday)
                return isDark ? WinColors.GCalHolidayBgDark : WinColors.GCalHolidayBg;
            if (dow == DayOfWeek.Sunday)
                return isDark ? WinColors.GCalSundayBgDark : WinColors.GCalSundayBg;
            if (dow == DayOfWeek.Saturday)
                return isDark ? WinColors.GCalSaturdayBgDark : WinColors.GCalSaturdayBg;
            return Windows.UI.Colors.Transparent;
        }
    }

    public static Color HeaderBackgroundColor => Windows.UI.Colors.Transparent;
    public Color HeaderTextColor => IsToday ? WinColors.GCalBlue : WinColors.FromHex("#5f6368");

    public ObservableCollection<WeekEventBlock> EventBlocks { get; }
    public ObservableCollection<WeekEventBlock> VisibleEventBlocks { get; } = [];
    public ObservableCollection<IWeekGuideLine> GuideLines { get; } = [];

    private int _lastStartMinute;
    private int _lastEndMinute = 24 * 60;

    public void UpdateVisibleRange(int startMinute, int endMinute, int bufferMinutes = 120)
    {
        _lastStartMinute = startMinute;
        _lastEndMinute = endMinute;
        var from = Math.Max(0, startMinute - bufferMinutes);
        var to = Math.Min(24 * 60, endMinute + bufferMinutes);
        VisibleEventBlocks.Clear();
        foreach (var block in EventBlocks.Where(e => e.EndMinute >= from && e.StartMinute <= to))
            VisibleEventBlocks.Add(block);
    }
}
