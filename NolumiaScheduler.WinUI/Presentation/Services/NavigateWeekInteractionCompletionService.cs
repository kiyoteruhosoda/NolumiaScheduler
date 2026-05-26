using NolumiaScheduler.Presentation.Controls;
using NolumiaScheduler.Presentation.Services;
using NolumiaScheduler.WinUI.Presentation.Pages;

namespace NolumiaScheduler.WinUI.Presentation.Services;

public sealed class NavigateWeekInteractionCompletionService : IWeekInteractionCompletionService
{
    // Set by CalendarPage so windows are tracked for GC safety.
    public Action<EventEditParams>? OpenEditWindowCallback { get; set; }

    public Task HandleDragCompletedAsync(WeekEventDragCompletedEventArgs args, CancellationToken cancellationToken = default)
    {
        OpenEditWindowCallback?.Invoke(new EventEditParams(
            EventId: args.EventId,
            OccurrenceDate: args.TargetDateTime.ToString("yyyy-MM-dd"),
            OccurrenceStartMinute: args.TargetStartMinute));
        return Task.CompletedTask;
    }

    public Task HandleResizeCompletedAsync(WeekEventResizeCompletedEventArgs args, CancellationToken cancellationToken = default)
    {
        OpenEditWindowCallback?.Invoke(new EventEditParams(
            EventId: args.EventId,
            OccurrenceDate: args.Date.ToString("yyyy-MM-dd"),
            OccurrenceStartMinute: args.StartMinute,
            OccurrenceEndMinute: args.EndMinute));
        return Task.CompletedTask;
    }
}
