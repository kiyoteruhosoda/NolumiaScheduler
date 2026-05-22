namespace NolumiaScheduler.Presentation.Services;

public sealed class WeekAutoScrollService : IWeekAutoScrollService
{
    private const double Edge = 48d;
    private const double MaxStep = 24d;

    public double ComputeVerticalDelta(double pointerY, double viewportHeight)
    {
        if (pointerY < Edge) return -Scale(Edge - pointerY);
        if (pointerY > viewportHeight - Edge) return Scale(pointerY - (viewportHeight - Edge));
        return 0;
    }

    private static double Scale(double distance)
        => Math.Min(MaxStep, Math.Max(2d, distance * 0.2d));
}
