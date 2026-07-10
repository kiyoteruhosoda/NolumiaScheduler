using NolumiaScheduler.Domain.ValueObjects;
using Windows.Foundation;
using Windows.UI;

namespace NolumiaScheduler.Presentation.ViewModels;

public sealed class WeekAllDayEventBlock : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsSelected))); }
    }
    public required string EventId { get; init; }
    public OccurrenceLocalKey? OccurrenceKey { get; init; }
    public required DateTime StartDate { get; init; }
    public required DateTime EndDate { get; init; }
    public required int LeftColumn { get; init; }
    public required int WidthColumns { get; init; }
    public required int Row { get; init; }
    public required string Title { get; init; }
    public required Color BackgroundColor { get; init; }
    public required double LeftRatio { get; init; }
    public required double WidthRatio { get; init; }
    /// <summary>
    /// <see langword="true"/> when this block represents a holiday from a business calendar
    /// rather than a user-created event. Holiday blocks are read-only (no tap-to-edit).
    /// </summary>
    public bool IsHoliday { get; init; }
    public bool IsRecurring { get; init; }
    public double Top => Row * 24;
    public static double Height => 22;
    public Rect LayoutBounds => new(LeftRatio, Top, WidthRatio, Height);
}
