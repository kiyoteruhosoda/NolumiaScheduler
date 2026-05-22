using NolumiaScheduler.Presentation.Controls;
using NolumiaScheduler.Presentation.Services;
using NolumiaScheduler.WinUI.Helpers;
using NolumiaScheduler.WinUI.Presentation.Pages;

namespace NolumiaScheduler.WinUI.Presentation.Services;

public sealed class NavigateWeekInteractionCompletionService : IWeekInteractionCompletionService
{
    public Task HandleDragCompletedAsync(WeekEventDragCompletedEventArgs args, CancellationToken cancellationToken = default)
    {
        NavigationService.Instance.Navigate(typeof(EventEditPage), new EventEditParams(
            EventId: args.EventId,
            OccurrenceDate: args.TargetDateTime.ToString("yyyy-MM-dd"),
            OccurrenceStartMinute: args.TargetStartMinute));
        return Task.CompletedTask;
    }

    public Task HandleResizeCompletedAsync(WeekEventResizeCompletedEventArgs args, CancellationToken cancellationToken = default)
    {
        NavigationService.Instance.Navigate(typeof(EventEditPage), new EventEditParams(
            EventId: args.EventId,
            OccurrenceDate: args.Date.ToString("yyyy-MM-dd"),
            OccurrenceStartMinute: args.StartMinute));
        return Task.CompletedTask;
    }
}
