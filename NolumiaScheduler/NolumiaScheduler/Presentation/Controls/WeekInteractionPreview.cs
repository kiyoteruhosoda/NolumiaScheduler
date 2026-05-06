namespace NolumiaScheduler.Presentation.Controls;

public sealed class WeekInteractionPreview
{
    public bool IsVisible { get; init; }
    public string? EventId { get; init; }
    public DateTime Date { get; init; }
    public int StartMinute { get; init; }
    public int EndMinute { get; init; }
}
