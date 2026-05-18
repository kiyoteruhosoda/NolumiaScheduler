using System.ComponentModel;
using System.Runtime.CompilerServices;
using NolumiaScheduler.Domain.ValueObjects;
using NolumiaScheduler.Presentation.Helpers;
using Windows.UI;

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
        IsToday ? WinColors.GCalBlue :
        IsSelected ? WinColors.GCalSelectedCircle :
        Microsoft.UI.Colors.Transparent;

    public Color DayTextColor
    {
        get
        {
            if (IsToday || IsSelected) return Microsoft.UI.Colors.White;
            var isDark = ThemeHelper.IsDark;
            if (IsHoliday && IsCurrentMonth)
                return isDark ? WinColors.GCalRedDark : WinColors.GCalRed;
            if (IsCurrentMonth)
                return isDark ? Microsoft.UI.Colors.White : WinColors.GCalTextPrimary;
            return isDark ? WinColors.GCalOutOfMonthTextDark : WinColors.GCalOutOfMonthText;
        }
    }

    public Color CellBackgroundColor
    {
        get
        {
            if (!IsCurrentMonth) return Microsoft.UI.Colors.Transparent;
            var isDark = ThemeHelper.IsDark;
            var dow = Date.DayOfWeek;
            if (IsHoliday)
                return isDark ? WinColors.GCalHolidayBgDark : WinColors.GCalHolidayBg;
            if (dow == DayOfWeek.Sunday)
                return isDark ? WinColors.GCalSundayBgDark : WinColors.GCalSundayBg;
            if (dow == DayOfWeek.Saturday)
                return isDark ? WinColors.GCalSaturdayBgDark : WinColors.GCalSaturdayBg;
            return Microsoft.UI.Colors.Transparent;
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
        if (occ == null) return Microsoft.UI.Colors.Transparent;
        if (occ.IsMoved) return WinColors.GCalEventMoved;
        if (occ.IsOverridden) return WinColors.GCalGreen;
        return WinColors.GCalBlue;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
