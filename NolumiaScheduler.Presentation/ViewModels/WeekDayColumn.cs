using System.Collections.ObjectModel;
using NolumiaScheduler.Presentation.Helpers;
using Windows.UI;

namespace NolumiaScheduler.Presentation.ViewModels;

public sealed class WeekDayColumn
{
    private const int MinutesPerDay = 24 * 60;

    public WeekDayColumn(
        string header, DateTime date, bool isHoliday, bool isToday = false, bool isPast = false)
    {
        Header = header;
        Date = date.Date;
        IsHoliday = isHoliday;
        IsToday = isToday;
        IsPast = isPast;
        EventBlocks = [];
        EventBlocks.CollectionChanged += (_, _) => UpdateVisibleRange(_lastStartMinute, _lastEndMinute);
    }

    public string Header { get; }
    public DateTime Date { get; }
    public bool IsHoliday { get; }
    public bool IsToday { get; }

    /// <summary>True when this column's whole day is behind us — i.e. it is before today.</summary>
    public bool IsPast { get; }

    /// <summary>
    /// Shade laid over the stretch of the week grid that has already passed, so a glance
    /// separates what is still ahead from what is done. Translucent, so the weekday / weekend /
    /// holiday background and the hour lines stay readable through it.
    /// </summary>
    public static Color PastShadeColor =>
        ThemeHelper.IsDark ? WinColors.GCalPastDayShadeDark : WinColors.GCalPastDayShade;

    /// <summary>
    /// How much of this column is already behind us, measured from midnight. The week grid is
    /// laid out one pixel per minute, so this doubles as the height of the past-time shade: a
    /// day already gone is shaded to the bottom, today down to the current-time line, and a day
    /// still ahead not at all.
    /// </summary>
    /// <param name="nowMinuteOfDay">Minutes from midnight to the current time.</param>
    /// <param name="dayHeight">Full height of one day column.</param>
    public double PastShadeHeight(double nowMinuteOfDay, double dayHeight) =>
        IsPast ? dayHeight
        : IsToday ? Math.Clamp(nowMinuteOfDay, 0, dayHeight)
        : 0;

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
            return WinColors.Transparent;
        }
    }

    public static Color HeaderBackgroundColor => WinColors.Transparent;
    public Color HeaderTextColor => IsToday ? WinColors.GCalBlue : WinColors.FromHex("#5f6368");

    public ObservableCollection<WeekEventBlock> EventBlocks { get; }
    public ObservableCollection<WeekEventBlock> VisibleEventBlocks { get; } = [];
    public ObservableCollection<IWeekGuideLine> GuideLines { get; } = [];

    private int _lastStartMinute;
    private int _lastEndMinute = MinutesPerDay;

    public void UpdateVisibleRange(int startMinute, int endMinute, int bufferMinutes = 120)
    {
        _lastStartMinute = startMinute;
        _lastEndMinute = endMinute;
        var from = Math.Max(0, startMinute - bufferMinutes);
        var to = Math.Min(MinutesPerDay, endMinute + bufferMinutes);
        VisibleEventBlocks.Clear();
        foreach (var block in EventBlocks.Where(e => e.EndMinute >= from && e.StartMinute <= to))
            VisibleEventBlocks.Add(block);
    }
}
