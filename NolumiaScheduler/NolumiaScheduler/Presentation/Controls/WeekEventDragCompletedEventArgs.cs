namespace NolumiaScheduler.Presentation.Controls;

public sealed class WeekEventDragCompletedEventArgs : EventArgs
{
    public required string EventId { get; init; }
    public required DateTime TargetDateTime { get; init; }
    public required int TargetStartMinute { get; init; }
}
