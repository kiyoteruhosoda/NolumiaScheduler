using System.Text.Json;
using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.Entities;
using NolumiaScheduler.Domain.ValueObjects;

namespace NolumiaScheduler.Infrastructure.Server;

/// <summary>One server call that brings the server's copy of an event closer to what the app saved.</summary>
public abstract record ServerEventStep
{
    public sealed record Create(EventCreateDto Body) : ServerEventStep;
    public sealed record UpdateSingle(EventUpdateDto Body) : ServerEventStep;
    public sealed record UpdateSeries(SeriesUpdateDto Body) : ServerEventStep;
    public sealed record Skip(OccurrenceActionDto Body) : ServerEventStep;
    public sealed record Restore(OccurrenceActionDto Body) : ServerEventStep;
    public sealed record Move(OccurrenceMoveDto Body) : ServerEventStep;
    public sealed record CancelMove(OccurrenceActionDto Body) : ServerEventStep;
}

/// <summary>
/// Turns "the app saved this aggregate" into the server's operation-shaped API (create, update,
/// update the series, skip/restore/move/cancel-move an occurrence). The repository contract is
/// "save the whole event" (decision 7 keeps it), so the planner diffs the aggregate against the
/// server's copy and returns the <em>next</em> step only; the caller applies it, takes the server's
/// answer as the new copy and asks again until <see cref="NextStep"/> returns null. Planning from
/// the server's latest answer each time means re-keyed exceptions (a series start-time change) and
/// version numbers are always the server's, never guessed.
/// </summary>
public static class ServerEventWritePlanner
{
    /// <summary>
    /// The next call to make, or null when the server already holds what the app saved.
    /// <paramref name="server"/> is null for an event the server does not have yet.
    /// </summary>
    public static ServerEventStep? NextStep(EventDto? server, CalendarEvent desired, long? calendarIdForNew)
    {
        if (desired.IsReadOnly)
            throw new ArgumentException("Imported events are never written to the server.", nameof(desired));

        if (server is null)
            return new ServerEventStep.Create(ServerEventMapper.ToCreateDto(desired, calendarIdForNew));

        if ((server.Kind == "RECURRING") != desired.IsRecurring())
            throw new InvalidOperationException("An event cannot change between single and recurring.");

        var current = ServerEventMapper.ToDomain(server, new BusinessCalendarId(ServerDaysOffCalendar.Id));

        if (desired.IsSingle())
        {
            var wanted = ServerEventMapper.ToUpdateDto(desired, server);
            return Same(wanted, ServerEventMapper.ToUpdateDto(current, server)) ? null : new ServerEventStep.UpdateSingle(wanted);
        }

        var series = ServerEventMapper.ToSeriesDto(desired, server);
        if (!Same(series, ServerEventMapper.ToSeriesDto(current, server)))
            return new ServerEventStep.UpdateSeries(series);

        var version = server.Version;
        var wantedSkips = SkipKeys(desired);
        var serverSkips = SkipKeys(current);
        if (wantedSkips.FirstOrDefault(k => !serverSkips.Contains(k)) is { } skip)
            return new ServerEventStep.Skip(new OccurrenceActionDto(ServerEventMapper.ToKeyDto(skip), version));
        if (serverSkips.FirstOrDefault(k => !wantedSkips.Contains(k)) is { } restore)
            return new ServerEventStep.Restore(new OccurrenceActionDto(ServerEventMapper.ToKeyDto(restore), version));

        var changedMove = desired.Moves.FirstOrDefault(move =>
            current.Moves.FirstOrDefault(m => m.OccurrenceKey.Equals(move.OccurrenceKey)) is not { } onServer
            || !Same(ServerEventMapper.ToMoveDto(move, desired), ServerEventMapper.ToMoveDto(onServer, current)));
        if (changedMove is not null)
            return new ServerEventStep.Move(ServerEventMapper.ToMoveRequest(changedMove, desired, version));

        var droppedMove = current.Moves.FirstOrDefault(m => !desired.Moves.Any(d => d.OccurrenceKey.Equals(m.OccurrenceKey)));
        if (droppedMove is not null)
            return new ServerEventStep.CancelMove(new OccurrenceActionDto(ServerEventMapper.ToKeyDto(droppedMove.OccurrenceKey), version));

        return null;
    }

    // Only skips are written. Overrides do not exist on the server's API (it splits the occurrence
    // into its own event instead), so legacy override rows are left alone.
    private static HashSet<OccurrenceLocalKey> SkipKeys(CalendarEvent ev)
        => [.. ev.Exceptions.Where(e => e.Type == ExceptionType.Skip).Select(e => e.OccurrenceKey)];

    // Compares the normalized wire forms, so "different" means "the server would store something else".
    private static bool Same<T>(T a, T b)
        => JsonSerializer.Serialize(a, ServerJson.Options) == JsonSerializer.Serialize(b, ServerJson.Options);
}
