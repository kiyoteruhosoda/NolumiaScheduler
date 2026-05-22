using Windows.Foundation;

namespace NolumiaScheduler.Presentation.Services;

public sealed class WeekGestureArbitrationService : IWeekGestureArbitrationService
{
    private const double TapDistanceThreshold = 8d;
    private static readonly TimeSpan TapDurationThreshold = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan LongPressThreshold = TimeSpan.FromMilliseconds(300);

    public WeekGestureDecision Decide(bool isOnEventBlock, bool isOnResizeHandle, Point start, Point current, TimeSpan elapsed, int touchCount = 1)
    {
        if (touchCount > 1) return WeekGestureDecision.Cancel;

        var dx = current.X - start.X;
        var dy = current.Y - start.Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);

        if (isOnResizeHandle && Math.Abs(dy) >= TapDistanceThreshold && Math.Abs(dy) > Math.Abs(dx))
            return WeekGestureDecision.Resize;

        if (isOnEventBlock && distance >= TapDistanceThreshold)
            return WeekGestureDecision.Drag;

        if (distance < TapDistanceThreshold && elapsed <= TapDurationThreshold)
            return WeekGestureDecision.Tap;

        if (distance < TapDistanceThreshold && elapsed >= LongPressThreshold)
            return WeekGestureDecision.LongPress;

        return isOnEventBlock ? WeekGestureDecision.None : WeekGestureDecision.Scroll;
    }
}
