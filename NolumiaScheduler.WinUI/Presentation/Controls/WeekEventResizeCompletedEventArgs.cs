namespace NolumiaScheduler.Presentation.Controls;

public sealed class WeekEventResizeCompletedEventArgs : EventArgs
{
    public required string EventId { get; init; }
    public required DateTime Date { get; init; }
    public required int StartMinute { get; init; }
    public required int EndMinute { get; init; }
}
