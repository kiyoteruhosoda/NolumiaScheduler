using Windows.Foundation;

namespace NolumiaScheduler.Presentation.Services;

public sealed class NoOpWeekDragInteractionService : IWeekDragInteractionService
{
    public void DragStart(string eventId, Point point) { }
    public void DragUpdate(Point point) { }
    public void DragEnd(Point point) { }
    public void ResizeStart(string eventId, Point point) { }
    public void ResizeEnd(Point point) { }
}
