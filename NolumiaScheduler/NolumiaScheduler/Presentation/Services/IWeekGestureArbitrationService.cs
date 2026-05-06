namespace NolumiaScheduler.Presentation.Services;

public enum WeekGestureDecision
{
    None,
    Tap,
    Drag,
    Resize,
    Scroll,
    LongPress,
    Cancel
}

public interface IWeekGestureArbitrationService
{
    WeekGestureDecision Decide(bool isOnEventBlock, bool isOnResizeHandle, Point start, Point current, TimeSpan elapsed, int touchCount = 1);
}
