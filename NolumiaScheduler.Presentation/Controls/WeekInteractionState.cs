namespace NolumiaScheduler.Presentation.Controls;

public enum WeekInteractionState
{
    Idle,
    Pressed,
    DraggingMove,
    DraggingResize,
    DraggingCreate,
    LongPressPending,
    Canceled
}
