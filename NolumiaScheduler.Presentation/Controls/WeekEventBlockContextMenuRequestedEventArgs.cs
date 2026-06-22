using NolumiaScheduler.Domain.ValueObjects;

namespace NolumiaScheduler.Presentation.Controls;

public sealed class WeekEventBlockContextMenuRequestedEventArgs : EventArgs
{
    public required string EventId { get; init; }
    public required DateTime Date { get; init; }
    public required int StartMinute { get; init; }
}