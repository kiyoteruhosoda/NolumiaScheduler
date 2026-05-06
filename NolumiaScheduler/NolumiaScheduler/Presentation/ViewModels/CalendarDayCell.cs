using System.ComponentModel;
using System.Runtime.CompilerServices;
using NolumiaScheduler.Domain.ValueObjects;

namespace NolumiaScheduler.Presentation.ViewModels;

public sealed class CalendarDayCell : INotifyPropertyChanged
{
    private bool _isSelected;

    public required LocalDateValue Date { get; init; }
    public bool IsToday { get; init; }
    public bool IsCurrentMonth { get; init; }
    public IReadOnlyList<EventOccurrence> Events { get; init; } = [];

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
            var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
            if (IsCurrentMonth)
                return isDark ? Colors.White : ResourceColor("GCalTextPrimary");
            return ResourceColor(isDark ? "GCalOutOfMonthTextDark" : "GCalOutOfMonthText");
        }
    }

    public EventOccurrence? FirstEvent => Events.Count > 0 ? Events[0] : null;
    public EventOccurrence? SecondEvent => Events.Count > 1 ? Events[1] : null;
    public int ExtraCount => Events.Count > 2 ? Events.Count - 2 : 0;

    public bool HasFirstEvent => FirstEvent != null;
    public bool HasSecondEvent => SecondEvent != null;
    public bool HasMoreEvents => ExtraCount > 0;
    public bool ShowSecondChip => HasSecondEvent && !HasMoreEvents;

    public string? FirstEventTitle => FirstEvent?.Title.Value;
    public string? SecondEventTitle => SecondEvent?.Title.Value;
    public string MoreText => $"+{ExtraCount}";

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
        if (Application.Current?.Resources.TryGetValue(key, out var val) == true && val is Color c)
            return c;
        return key switch
        {
            "GCalBlue"               => Color.FromArgb("#1a73e8"),
            "GCalSelectedCircle"     => Color.FromArgb("#70757a"),
            "GCalTextPrimary"        => Color.FromArgb("#202124"),
            "GCalGreen"              => Color.FromArgb("#1e8e3e"),
            "GCalEventMoved"         => Color.FromArgb("#9c27b0"),
            "GCalOutOfMonthText"     => Color.FromArgb("#bdbdbd"),
            "GCalOutOfMonthTextDark" => Color.FromArgb("#555555"),
            _ => Colors.Gray
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
