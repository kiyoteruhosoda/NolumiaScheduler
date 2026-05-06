namespace NolumiaScheduler.Presentation.ViewModels;

public sealed class WeekEventBlock
{
    public required string EventId { get; init; }
    public required string OccurrenceKey { get; init; }
    public required string Title { get; init; }
    public required string TimeLabel { get; init; }
    public required Color BackgroundColor { get; init; }
    public required double Top { get; init; }
    public required double Height { get; init; }
    public required double LeftRatio { get; init; }
    public required double WidthRatio { get; init; }
}
