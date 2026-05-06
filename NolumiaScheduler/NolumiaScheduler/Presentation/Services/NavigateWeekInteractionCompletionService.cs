using NolumiaScheduler.Presentation.Controls;

namespace NolumiaScheduler.Presentation.Services;

public sealed class NavigateWeekInteractionCompletionService : IWeekInteractionCompletionService
{
    public Task HandleDragCompletedAsync(WeekEventDragCompletedEventArgs args, CancellationToken cancellationToken = default)
        => Shell.Current.GoToAsync($"EventEdit?eventId={args.EventId}&occurrenceDate={args.TargetDateTime:yyyy-MM-dd}&occurrenceStartMinute={args.TargetStartMinute}");

    public Task HandleResizeCompletedAsync(WeekEventResizeCompletedEventArgs args, CancellationToken cancellationToken = default)
        => Shell.Current.GoToAsync($"EventEdit?eventId={args.EventId}&occurrenceDate={args.Date:yyyy-MM-dd}&occurrenceStartMinute={args.StartMinute}");
}
