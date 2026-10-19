using System.Globalization;
using System.Text.Json;
using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.Exceptions;
using NolumiaScheduler.Domain.ValueObjects;

namespace NolumiaScheduler.Infrastructure.Server;

/// <summary>Everything the app reads from the server, as the server sent it. Also the shape of <c>server-cache.json</c>.</summary>
public sealed record ServerSnapshot(
    IReadOnlyList<EventDto> Events,
    IReadOnlyList<CalendarDto> Calendars,
    IReadOnlyList<DayOffMarkDto> DayOffMarks,
    IReadOnlyList<ImportedOccurrenceDto> ImportedOccurrences,
    DateTimeOffset? FetchedAt)
{
    public static ServerSnapshot Empty { get; } = new([], [], [], [], null);
}

/// <summary>
/// The local copy of the server (decision 7): kept in memory and in <c>server-cache.json</c>, refreshed
/// at startup, every few minutes and after each write. While the server cannot be reached the copy keeps
/// the calendar and the alarms going. Writes go to the server only when it answers — nothing is queued
/// for later — and the copy takes the server's answer, so it never holds anything the server does not.
/// <para>
/// Threading: <see cref="RefreshAsync"/>, <see cref="Save"/> and <see cref="Delete"/> do their network
/// calls off the caller's context and apply the result back on it, so <see cref="Changed"/> fires on the
/// thread that called them (the UI thread in the app).
/// </para>
/// </summary>
public sealed class ServerCalendarStore
{
    private const int MaxStepsPerSave = 50;

    private readonly SchedulerApiClient _api;
    private readonly string _cacheFile;
    private readonly TimeProvider _clock;
    private readonly string _timeZoneId;
    private readonly BusinessCalendarId _daysOffId = new(ServerDaysOffCalendar.Id);
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _aliases = [];

    private State _state;

    public ServerCalendarStore(SchedulerApiClient api, string cacheFile, TimeProvider clock, string timeZoneId)
    {
        _api = api;
        _cacheFile = cacheFile;
        _clock = clock;
        _timeZoneId = timeZoneId;
        _state = Build(LoadCache());
    }

    /// <summary>Raised when the copy changed (after a refresh that brought something new, or a write).</summary>
    public event Action? Changed;

    /// <summary>Calendar that new events go to; null = the server's default calendar.</summary>
    public long? NewEventCalendarId { get; set; }

    public ServerSnapshot Snapshot => _state.Snapshot;

    /// <summary>When the copy was last read from the server (null = never; the app shows nothing yet).</summary>
    public DateTimeOffset? FetchedAt => _state.Snapshot.FetchedAt;

    public BusinessCalendar DaysOff => _state.DaysOff;

    public IReadOnlyList<CalendarEvent> Events => _state.Events;

    public IReadOnlyList<CalendarEvent> ImportedEvents => _state.Imported;

    /// <summary>The server's copy of an event (by the app's id, including a not-yet-replaced id of a new event).</summary>
    public EventDto? FindDto(string eventId)
    {
        lock (_gate)
        {
            var id = _aliases.TryGetValue(eventId, out var alias) ? alias : eventId;
            return _state.Dtos.TryGetValue(id, out var dto) ? dto : null;
        }
    }

    /// <summary>A fresh aggregate from the server's copy; callers may change it freely before saving.</summary>
    public CalendarEvent? FindEvent(string eventId)
        => FindDto(eventId) is { } dto ? ServerEventMapper.ToDomain(dto, _daysOffId) : null;

    // ── Reading ──────────────────────────────────────────────────────────

    /// <summary>
    /// Reads everything again. Throws <see cref="ServerException"/> when the server cannot be read; the
    /// previous copy is then kept as it was. <see cref="Changed"/> fires only if something differs, so
    /// a quiet refresh does not reset the alarm state.
    /// </summary>
    public async Task RefreshAsync(CancellationToken ct)
    {
        var snapshot = await FetchAsync(ct).ConfigureAwait(true);
        if (Comparable(snapshot) == Comparable(_state.Snapshot))
        {
            Replace(snapshot, raise: false);
            return;
        }
        Replace(snapshot, raise: true);
    }

    private async Task<ServerSnapshot> FetchAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
        var calendars = await _api.ListCalendarsAsync(ct).ConfigureAwait(false);
        // Events: every event (the server pre-filters by period, so ask for all of it).
        var events = await _api.ListEventsAsync(new DateOnly(1970, 1, 1), new DateOnly(9999, 1, 1), ct).ConfigureAwait(false);
        // Day-off marks and imported occurrences are limited to 800 days per request on the server.
        var marks = await _api.ListDayOffMarksAsync(today.AddDays(-365), today.AddDays(365), ct).ConfigureAwait(false);
        var imported = await _api.ListImportedOccurrencesAsync(today.AddDays(-60), today.AddDays(400), ct).ConfigureAwait(false);
        return new ServerSnapshot(events, calendars, marks, imported, _clock.GetUtcNow());
    }

    // ── Writing ──────────────────────────────────────────────────────────
    // The repository contract is synchronous (decision 7 keeps it), so writes block the caller until
    // the server answers. The network part runs on the thread pool; the copy is updated and
    // Changed raised back on the calling thread.

    /// <summary>
    /// Makes the server hold <paramref name="desired"/>, one server call at a time
    /// (<see cref="ServerEventWritePlanner"/>). Throws <see cref="ServerException"/> when the server
    /// refuses or cannot be reached; on a 409 the copy is read again first, so the next attempt starts
    /// from the server's current version.
    /// </summary>
    public void Save(CalendarEvent desired)
    {
        var originalId = desired.Id.Value;
        var server = FindDto(originalId);
        var calendarId = NewEventCalendarId;
        var (latest, error) = Task.Run(() => PushAsync(server, desired, calendarId, CancellationToken.None))
            .GetAwaiter().GetResult();

        // Whatever the server holds now — even after a failure half-way — is what the copy shows.
        if (latest is not null) Put(latest, originalId);
        else Changed?.Invoke();

        if (error is null) return;
        if (error.IsConflict)
        {
            TryRefreshBlocking();
            throw new ServerException(
                "This event was changed elsewhere. The latest version was loaded; please make the change again.",
                error.StatusCode, error.Reason, error);
        }
        throw error;
    }

    public void Delete(string eventId)
    {
        if (FindDto(eventId) is not { } dto) return;
        var error = Task.Run<ServerException?>(async () =>
        {
            try
            {
                await _api.DeleteEventAsync(dto.Id, CancellationToken.None).ConfigureAwait(false);
                return null;
            }
            catch (ServerException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null; // already gone on the server
            }
            catch (ServerException ex)
            {
                return ex;
            }
        }).GetAwaiter().GetResult();

        if (error is not null)
        {
            Changed?.Invoke();
            throw error;
        }

        lock (_gate)
        {
            var events = _state.Snapshot.Events.Where(e => e.Id != dto.Id).ToList();
            ReplaceLocked(_state.Snapshot with { Events = events });
        }
        SaveCache();
        Changed?.Invoke();
    }

    private async Task<(EventDto? Latest, ServerException? Error)> PushAsync(
        EventDto? server, CalendarEvent desired, long? calendarIdForNew, CancellationToken ct)
    {
        var start = server;
        string? previous = null;
        try
        {
            for (var steps = 0; steps < MaxStepsPerSave; steps++)
            {
                var step = ServerEventWritePlanner.NextStep(server, desired, calendarIdForNew);
                if (step is null) break;
                // A step that repeats would loop forever (the server normalized something the planner
                // did not expect); what the server holds is then the best we can do.
                var fingerprint = step.GetType().Name + JsonSerializer.Serialize(step, step.GetType(), ServerJson.Options);
                if (fingerprint == previous) break;
                server = await ApplyAsync(server, step, ct).ConfigureAwait(false);
                previous = fingerprint;
            }
            return (server, null);
        }
        catch (ServerException ex)
        {
            return (ReferenceEquals(server, start) ? null : server, ex);
        }
    }

    private async Task<EventDto> ApplyAsync(EventDto? server, ServerEventStep step, CancellationToken ct)
    {
        var id = server?.Id ?? 0;
        return step switch
        {
            ServerEventStep.Create s => await _api.CreateEventAsync(s.Body, ct).ConfigureAwait(false),
            ServerEventStep.UpdateSingle s => await _api.UpdateEventAsync(id, s.Body, ct).ConfigureAwait(false),
            ServerEventStep.UpdateSeries s => await _api.UpdateSeriesAsync(id, s.Body, ct).ConfigureAwait(false),
            ServerEventStep.Skip s => await _api.SkipOccurrenceAsync(id, s.Body, ct).ConfigureAwait(false),
            ServerEventStep.Restore s => await _api.RestoreOccurrenceAsync(id, s.Body, ct).ConfigureAwait(false),
            ServerEventStep.Move s => await _api.MoveOccurrenceAsync(id, s.Body, ct).ConfigureAwait(false),
            ServerEventStep.CancelMove s => await _api.CancelMoveOccurrenceAsync(id, s.Body, ct).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unknown step {step}"),
        };
    }

    private void TryRefreshBlocking()
    {
        try
        {
            var snapshot = Task.Run(() => FetchAsync(CancellationToken.None)).GetAwaiter().GetResult();
            Replace(snapshot, raise: true);
        }
        catch (ServerException)
        {
            // The conflict error below still tells the user; the next periodic refresh catches up.
        }
    }

    /// <summary>Keeps the read-only imported event changed in the app (alarm toggles) until the next refresh.</summary>
    public void PutImported(CalendarEvent imported)
    {
        lock (_gate)
        {
            var list = _state.Imported.Select(e => e.Id.Value == imported.Id.Value ? imported : e).ToList();
            _state = _state with { Imported = list };
        }
        Changed?.Invoke();
    }

    private void Put(EventDto saved, string savedAs)
    {
        lock (_gate)
        {
            var key = saved.Id.ToString(CultureInfo.InvariantCulture);
            if (savedAs != key) _aliases[savedAs] = key;
            var events = _state.Snapshot.Events.Where(e => e.Id != saved.Id).Append(saved).ToList();
            ReplaceLocked(_state.Snapshot with { Events = events });
        }
        SaveCache();
        Changed?.Invoke();
    }

    // ── State ────────────────────────────────────────────────────────────

    private sealed record State(
        ServerSnapshot Snapshot,
        IReadOnlyDictionary<string, EventDto> Dtos,
        IReadOnlyList<CalendarEvent> Events,
        IReadOnlyList<CalendarEvent> Imported,
        BusinessCalendar DaysOff);

    private void Replace(ServerSnapshot snapshot, bool raise)
    {
        lock (_gate) ReplaceLocked(snapshot);
        SaveCache();
        if (raise) Changed?.Invoke();
    }

    private void ReplaceLocked(ServerSnapshot snapshot) => _state = Build(snapshot);

    private State Build(ServerSnapshot snapshot)
    {
        var dtos = new Dictionary<string, EventDto>();
        var events = new List<CalendarEvent>();
        foreach (var dto in snapshot.Events)
        {
            // One unreadable event must not hide the rest of the calendar.
            try
            {
                events.Add(ServerEventMapper.ToDomain(dto, _daysOffId));
                dtos[dto.Id.ToString(CultureInfo.InvariantCulture)] = dto;
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException or TimeZoneNotFoundException or DomainException)
            {
            }
        }

        var imported = new List<CalendarEvent>();
        foreach (var dto in snapshot.ImportedOccurrences)
        {
            try
            {
                imported.Add(ServerEventMapper.ToDomain(dto, _timeZoneId));
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException or DomainException)
            {
            }
        }

        return new State(snapshot, dtos, events, imported,
            ServerDaysOffCalendar.Build(snapshot.Calendars, snapshot.DayOffMarks, _timeZoneId));
    }

    private static string Comparable(ServerSnapshot snapshot)
        => JsonSerializer.Serialize(snapshot with { FetchedAt = null }, ServerJson.Options);

    private ServerSnapshot LoadCache()
    {
        try
        {
            if (!File.Exists(_cacheFile)) return ServerSnapshot.Empty;
            return JsonSerializer.Deserialize<ServerSnapshot>(File.ReadAllText(_cacheFile), ServerJson.Options)
                ?? ServerSnapshot.Empty;
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
        {
            // A broken copy only costs a refresh.
            return ServerSnapshot.Empty;
        }
    }

    private void SaveCache()
    {
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(_cacheFile));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var temporary = _cacheFile + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_state.Snapshot, ServerJson.Options));
            File.Move(temporary, _cacheFile, overwrite: true);
        }
        catch (IOException)
        {
            // The in-memory copy is still right; the file is only for the next start without network.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
