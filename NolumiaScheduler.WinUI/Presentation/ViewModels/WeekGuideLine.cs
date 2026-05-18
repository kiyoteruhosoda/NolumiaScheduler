namespace NolumiaScheduler.Presentation.ViewModels;

public interface IWeekGuideLine
{
    double Top { get; }
    bool IsPrimary { get; }
    Windows.Foundation.Rect LayoutBounds { get; }
}

public sealed class HourGuideLine(int hour) : IWeekGuideLine
{
    public double Top { get; } = hour * 60d;
    public bool IsPrimary => true;
    public Windows.Foundation.Rect LayoutBounds => new(0, Top, 1, 1);
}

public sealed class HalfHourGuideLine(int hour) : IWeekGuideLine
{
    public double Top { get; } = (hour * 60d) + 30d;
    public bool IsPrimary => false;
    public Windows.Foundation.Rect LayoutBounds => new(0, Top, 1, 1);
}
