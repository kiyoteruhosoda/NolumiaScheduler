using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Maui.Controls;
using NolumiaScheduler.Domain.ValueObjects;
using MauiApplication = Microsoft.Maui.Controls.Application;

namespace NolumiaScheduler.Presentation.ViewModels;

public sealed class CalendarDayCell : INotifyPropertyChanged
{
    private bool _isSelected;

    public required LocalDateValue Date { get; init; }
    public bool IsToday { get; init; }
    public bool IsCurrentMonth { get; init; }
    public IReadOnlyList<EventOccurrence> Events { get; init; } = [];
    public bool IsHoliday { get; init; }
    public string? HolidayName { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CircleColor));
            OnPropertyChanged(nameof(DayTextColor));
            OnPropertyChanged(nameof(CellBackgroundColor));
        }
    }

    public string DayLabel => Date.Day.ToString();

    public Color CircleColor =>
        IsToday ? ResourceColor("GCalBlue") :
        IsSelected ? ResourceColor("GCalSelectedCircle") :
        Colors.Transparent;

    public Color DayTextColor
    {
        get
        {
            if (IsToday || IsSelected) return Colors.White;
            var isDark = MauiApplication.Current?.RequestedTheme == AppTheme.Dark;
            if (IsHoliday && IsCurrentMonth)
                return isDark ? ResourceColor("GCalRedDark") : ResourceColor("GCalRed");
            if (IsCurrentMonth)
                return isDark ? Colors.White : ResourceColor("GCalTextPrimary");
            return ResourceColor(isDark ? "GCalOutOfMonthTextDark" : "GCalOutOfMonthText");
        }
    }

    public Color CellBackgroundColor
    {
        get
        {
            if (!IsCurrentMonth) return Colors.Transparent;
            var isDark = MauiApplication.Current?.RequestedTheme == AppTheme.Dark;
            var dow = Date.DayOfWeek;
            if (IsHoliday)
                return isDark ? ResourceColor("GCalHolidayBgDark") : ResourceColor("GCalHolidayBg");
            if (dow == DayOfWeek.Sunday)
                return isDark ? ResourceColor("GCalSundayBgDark") : ResourceColor("GCalSundayBg");
            if (dow == DayOfWeek.Saturday)
                return isDark ? ResourceColor("GCalSaturdayBgDark") : ResourceColor("GCalSaturdayBg");
            return Colors.Transparent;
        }
    }

    public EventOccurrence? FirstEvent => Events.Count > 0 ? Events[0] : null;
    public EventOccurrence? SecondEvent => Events.Count > 1 ? Events[1] : null;
    public int ExtraCount => Events.Count > 2 ? Events.Count - 2 : 0;

    private int _availableChipCount = 2;
    public int AvailableChipCount
    {
        get => _availableChipCount;
        set
        {
            if (_availableChipCount == value) return;
            _availableChipCount = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VisibleEvents));
            OnPropertyChanged(nameof(VisibleExtraCount));
            OnPropertyChanged(nameof(HasMoreEvents));
            OnPropertyChanged(nameof(ShowSecondChip));
            OnPropertyChanged(nameof(MoreText));
        }
    }

    public IReadOnlyList<CalendarEventChip> VisibleEvents =>
        [.. Events.Take(_availableChipCount).Select(e => new CalendarEventChip(e))];

    public int VisibleExtraCount =>
        Events.Count > _availableChipCount ? Events.Count - _availableChipCount : 0;
    public bool HasSecondEvent => SecondEvent != null;
    public bool HasMoreEvents => VisibleExtraCount > 0;
    public bool ShowSecondChip => HasSecondEvent && !HasMoreEvents;

    public string? FirstEventTitle => FirstEvent?.Title.Value;
    public string? SecondEventTitle => SecondEvent?.Title.Value;
    public string MoreText => $"+{VisibleExtraCount}";

    public Color FirstEventColor => EventChipColor(FirstEvent);
    public Color SecondEventColor => EventChipColor(SecondEvent);

    private static Color EventChipColor(EventOccurrence? occ)
    {
        if (occ == null) return Colors.Transparent;
        if (occ.IsMoved) return ResourceColor("GCalEventMoved");
        if (occ.IsOverridden) return ResourceColor("GCalGreen");
        return ResourceColor("GCalBlue");
    }

    private static Color ResourceColor(string key)
    {
        if (MauiApplication.Current?.Resources.TryGetValue(key, out var val) == true && val is Color c)
            return c;
        return key switch
        {
            "GCalBlue"               => Color.FromArgb("#1a73e8"),
            "GCalSelectedCircle"     => Color.FromArgb("#70757a"),
            "GCalTextPrimary"        => Color.FromArgb("#202124"),
            "GCalGreen"              => Color.FromArgb("#1e8e3e"),
            "GCalEventMoved"         => Color.FromArgb("#9c27b0"),
            "GCalRed"                => Color.FromArgb("#d93025"),
            "GCalRedDark"            => Color.FromArgb("#f28b82"),
            "GCalHolidayBg"          => Color.FromArgb("#fff0f0"),
            "GCalHolidayBgDark"      => Color.FromArgb("#3a1a1a"),
            "GCalSundayBg"           => Color.FromArgb("#fff8f8"),
            "GCalSundayBgDark"       => Color.FromArgb("#2d1a1a"),
            "GCalSaturdayBg"         => Color.FromArgb("#f0f4ff"),
            "GCalSaturdayBgDark"     => Color.FromArgb("#1a1a2d"),
            "GCalOutOfMonthText"     => Color.FromArgb("#bdbdbd"),
            "GCalOutOfMonthTextDark" => Color.FromArgb("#555555"),
            _ => Colors.Gray
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
