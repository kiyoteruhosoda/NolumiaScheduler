using NolumiaScheduler.Domain.ValueObjects;
using Windows.Foundation;
using Windows.UI;

namespace NolumiaScheduler.Presentation.ViewModels;

public sealed class WeekEventBlock : System.ComponentModel.INotifyPropertyChanged
{
    public required string EventId { get; init; }
    public OccurrenceLocalKey? OccurrenceKey { get; init; }

    // Original recurrence key used to target this occurrence when moving/resizing.
    public OccurrenceLocalKey? MoveKey { get; init; }
    public required DateTime Date { get; init; }
    public required int StartMinute { get; init; }
    public required int EndMinute { get; init; }
    public required string Title { get; init; }
    public required string? Location { get; init; }
    public required string TimeLabel { get; init; }
    public required Color BackgroundColor { get; init; }
    public required double Top { get; init; }
    public required double Height { get; init; }
    public required double LeftRatio { get; init; }
    public required double WidthRatio { get; init; }
    public required Rect Bounds { get; init; }

    private Rect _layoutBounds;
    public Rect LayoutBounds
    {
        get => _layoutBounds;
        private set
        {
            if (_layoutBounds == value) return;
            _layoutBounds = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(LayoutBounds)));
        }
    }
    public required Rect ResizeHandleBounds { get; init; }

    private bool _isResizePreview;
    public bool IsResizePreview
    {
        get => _isResizePreview;
        set { _isResizePreview = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsResizePreview))); }
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsSelected))); }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public void UpdateLayoutBounds(double weekDayColumnWidth)
    {
        var safeWidth = Math.Max(0, weekDayColumnWidth);
        var leftPx = LeftRatio * safeWidth;
        var widthPx = Math.Max(0, WidthRatio * safeWidth);
        LayoutBounds = new Rect(leftPx, Top, widthPx, Height);
    }
}
