using NolumiaScheduler.Presentation.Controls;

namespace NolumiaScheduler.Presentation.Services;

public interface IWeekInteractionCompletionService
{
    Task HandleDragCompletedAsync(WeekEventDragCompletedEventArgs args, CancellationToken cancellationToken = default);
    Task HandleResizeCompletedAsync(WeekEventResizeCompletedEventArgs args, CancellationToken cancellationToken = default);
}
