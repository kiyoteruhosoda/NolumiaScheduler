using NolumiaScheduler.Domain.ValueObjects;

namespace NolumiaScheduler.Presentation.Controls;

public sealed class WeekEventResizeCompletedEventArgs : EventArgs
{
    public required string EventId { get; init; }
    public OccurrenceLocalKey? OccurrenceKey { get; init; }
    public required DateTime Date { get; init; }
    public required int StartMinute { get; init; }
    public required int EndMinute { get; init; }
    // Original time bounds before the resize started (used for undo).
    public required int OriginalStartMinute { get; init; }
    public required int OriginalEndMinute { get; init; }
}
