namespace NolumiaScheduler.Presentation.Controls;

public sealed class WeekSlotDragCreatedEventArgs : EventArgs
{
    public required DateTime Date { get; init; }
    public required int StartMinute { get; init; }
    public required int EndMinute { get; init; }
}
