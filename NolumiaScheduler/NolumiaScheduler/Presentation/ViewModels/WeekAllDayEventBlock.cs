using NolumiaScheduler.Domain.ValueObjects;

namespace NolumiaScheduler.Presentation.ViewModels;

public sealed class WeekAllDayEventBlock
{
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
    public double Top => Row * 24;
    public double Height => 22;
}
