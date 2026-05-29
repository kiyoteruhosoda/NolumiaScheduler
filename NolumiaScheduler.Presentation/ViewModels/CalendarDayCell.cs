using System.ComponentModel;
using System.Runtime.CompilerServices;
using NolumiaScheduler.Domain.ValueObjects;
using NolumiaScheduler.Presentation.Helpers;
using Windows.UI;

namespace NolumiaScheduler.Presentation.ViewModels;

public sealed partial class CalendarDayCell : INotifyPropertyChanged
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

    private double _cellHeight = 88;
    public double CellHeight
    {
        get => _cellHeight;
        set
        {
            if (Math.Abs(_cellHeight - value) < 0.5) return;
            _cellHeight = value;
            OnPropertyChanged();
        }
    }

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
            OnPropertyChanged(nameof(MoreText));
        }
    }

    // How many event chips to actually draw. If every event fits in the available rows,
    // show them all; otherwise reserve the last row for the "+N more" label so the visible
    // chip count matches the space and we don't show "+N" while rows are still empty.
    private int VisibleChipCount =>
        Events.Count <= _availableChipCount
            ? Events.Count
            : Math.Max(1, _availableChipCount - 1);

    public IReadOnlyList<CalendarEventChip> VisibleEvents =>
        [.. Events.Take(VisibleChipCount).Select(e => new CalendarEventChip(e))];

    public int VisibleExtraCount => Events.Count - VisibleChipCount;
    public bool HasMoreEvents => VisibleExtraCount > 0;
    public string MoreText => $"+{VisibleExtraCount}";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
