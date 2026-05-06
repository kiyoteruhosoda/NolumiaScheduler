using NolumiaScheduler.Domain.ValueObjects;

namespace NolumiaScheduler.Presentation.Controls;

public sealed class WeekEventBlockTappedEventArgs : EventArgs
{
    public required string EventId { get; init; }
    public OccurrenceLocalKey? OccurrenceKey { get; init; }
}
