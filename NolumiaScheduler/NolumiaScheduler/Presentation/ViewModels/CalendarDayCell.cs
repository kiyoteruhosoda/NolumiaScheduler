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
        IsToday ? Color.FromArgb("#1a73e8") :
        IsSelected ? Color.FromArgb("#70757a") :
        Colors.Transparent;

    public Color DayTextColor =>
        (IsToday || IsSelected) ? Colors.White :
        IsCurrentMonth ? Color.FromArgb("#202124") :
        Color.FromArgb("#b0b0b0");

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
        if (occ.IsMoved) return Color.FromArgb("#9c27b0");
        if (occ.IsOverridden) return Color.FromArgb("#1e8e3e");
        return Color.FromArgb("#1a73e8");
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
