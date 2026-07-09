using NolumiaScheduler.Domain.ValueObjects;

namespace NolumiaScheduler.Presentation.Controls;

public sealed class WeekEventDragCompletedEventArgs : EventArgs
{
    public required string EventId { get; init; }
    public OccurrenceLocalKey? OccurrenceKey { get; init; }
    public required DateTime TargetDateTime { get; init; }
    public required int TargetStartMinute { get; init; }
    public required int DurationMinutes { get; init; }
    // Original position before the drag started (used for undo).
    public required DateTime OriginalDate { get; init; }
    public required int OriginalStartMinute { get; init; }
    public required int OriginalEndMinute { get; init; }
}
