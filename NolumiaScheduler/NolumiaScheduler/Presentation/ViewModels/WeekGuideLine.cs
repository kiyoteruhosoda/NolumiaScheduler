namespace NolumiaScheduler.Presentation.ViewModels;

public interface IWeekGuideLine
{
    double Top { get; }
    bool IsPrimary { get; }
    Microsoft.Maui.Graphics.Rect LayoutBounds { get; }
}

public sealed class HourGuideLine : IWeekGuideLine
{
    public HourGuideLine(int hour) => Top = hour * 60d;
    public double Top { get; }
    public bool IsPrimary => true;
    public Microsoft.Maui.Graphics.Rect LayoutBounds => new Microsoft.Maui.Graphics.Rect(0, Top, 1, 1);
}

public sealed class HalfHourGuideLine : IWeekGuideLine
{
    public HalfHourGuideLine(int hour) => Top = (hour * 60d) + 30d;
    public double Top { get; }
    public bool IsPrimary => false;
    public Microsoft.Maui.Graphics.Rect LayoutBounds => new Microsoft.Maui.Graphics.Rect(0, Top, 1, 1);
}
