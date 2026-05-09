using System.Collections.ObjectModel;
using System.Linq;
using MauiApplication = Microsoft.Maui.Controls.Application;

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
            var isDark = MauiApplication.Current?.RequestedTheme == AppTheme.Dark;
            var dow = Date.DayOfWeek;
            if (IsHoliday && dow != DayOfWeek.Saturday)
                return isDark ? ResourceColor("GCalHolidayBgDark") : ResourceColor("GCalHolidayBg");
            if (dow == DayOfWeek.Sunday)
                return isDark ? ResourceColor("GCalSundayBgDark") : ResourceColor("GCalSundayBg");
            if (dow == DayOfWeek.Saturday)
                return isDark ? ResourceColor("GCalSaturdayBgDark") : ResourceColor("GCalSaturdayBg");
            return Colors.Transparent;
        }
    }
    public FontAttributes HeaderFontAttributes => IsToday ? FontAttributes.Bold : FontAttributes.None;
    public Color HeaderBackgroundColor => Colors.Transparent;
    public Color HeaderTextColor => IsToday ? Color.FromArgb("#1a73e8") : Color.FromArgb("#5f6368");
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

    private static Color ResourceColor(string key)
    {
        if (MauiApplication.Current?.Resources.TryGetValue(key, out var val) == true && val is Color c)
            return c;
        return key switch
        {
            "GCalHolidayBg"      => Color.FromArgb("#fff0f0"),
            "GCalHolidayBgDark"  => Color.FromArgb("#3a1a1a"),
            "GCalSundayBg"       => Color.FromArgb("#fff8f8"),
            "GCalSundayBgDark"   => Color.FromArgb("#2d1a1a"),
            "GCalSaturdayBg"     => Color.FromArgb("#f0f4ff"),
            "GCalSaturdayBgDark" => Color.FromArgb("#1a1a2d"),
            _                    => Colors.Transparent
        };
    }
}
