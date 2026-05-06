namespace NolumiaScheduler.Presentation.Services;

public interface IWeekDragInteractionService
{
    void DragStart(string eventId, Point point);
    void DragUpdate(Point point);
    void DragEnd(Point point);
    void ResizeStart(string eventId, Point point);
    void ResizeEnd(Point point);
}
