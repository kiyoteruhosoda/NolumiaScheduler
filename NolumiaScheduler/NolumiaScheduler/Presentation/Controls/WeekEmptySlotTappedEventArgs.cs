namespace NolumiaScheduler.Presentation.Controls;

public sealed class WeekEmptySlotTappedEventArgs : EventArgs
{
    public required DateTime Date { get; init; }
    public required int StartMinute { get; init; }
}
