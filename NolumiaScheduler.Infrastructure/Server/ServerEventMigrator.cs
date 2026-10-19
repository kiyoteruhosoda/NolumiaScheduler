using System.Text.Json;
using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.Entities;
using NolumiaScheduler.Domain.Repositories;
using NolumiaScheduler.Domain.ValueObjects;

namespace NolumiaScheduler.Infrastructure.Server;

/// <summary>What one run of the move to the server did. Also the content of <c>migrated-to-server.json</c>.</summary>
public sealed record ServerMigrationReport(
    int Sent,
    int Created,
    int AlreadyOnServer,
    int SentToPrivate,
    int SkippedImported,
    DateTimeOffset MigratedAt,
    string Server);

/// <summary>
/// Moves the PC's events to the server once (server ADR-0054 decision 8, #537 AC2). Reads the
/// current local store (JSON or SQLite through <see cref="ICalendarEventRepository"/>) and sends
/// every event to <c>POST /api/calendar/events/import</c> with <c>client_ref</c> = the local
/// <see cref="EventId"/>; the server creates an event only for a <c>client_ref</c> it has not seen,
/// so running it again after a failure continues without duplicates.
/// <list type="bullet">
/// <item>Events imported from Outlook (<see cref="CalendarEvent.ExternalOrigin"/>) are not moved — the
/// Outlook feed puts them on the server (decision 6).</item>
/// <item>Visibility Private/Confidential goes to a private calendar (created as
/// "<see cref="PrivateCalendarName"/>" when there is none); everything else to the default (work) calendar.</item>
/// <item>The named business calendar of a business-day shift is dropped; the shift stays and the
/// server's day-off layers decide (server ADR-0032).</item>
/// <item>Event type, visibility itself, version and creation time are not moved.</item>
/// </list>
/// When everything is on the server, <c>migrated-to-server.json</c> is written. The local files
/// are never deleted (they stay as a backup; the owner deletes them).
/// </summary>
public sealed class ServerEventMigrator(SchedulerApiClient api, TimeProvider clock, int batchSize = 50)
{
    public const string MarkerFileName = "migrated-to-server.json";
    public const string PrivateCalendarName = "プライベート（PC から）";

    /// <summary>The server accepts up to 100 events per import request.</summary>
    public const int MaxBatchSize = 100;

    private readonly int _batchSize = Math.Clamp(batchSize, 1, MaxBatchSize);

    public static bool IsMigrated(string dataDirectory) => File.Exists(Path.Combine(dataDirectory, MarkerFileName));

    public static ServerMigrationReport? ReadMarker(string dataDirectory)
    {
        try
        {
            var path = Path.Combine(dataDirectory, MarkerFileName);
            return File.Exists(path)
                ? JsonSerializer.Deserialize<ServerMigrationReport>(File.ReadAllText(path), ServerJson.Options)
                : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Sends every local event. Throws <see cref="ServerException"/> when the server cannot be reached
    /// or refuses; nothing is marked then, and running again continues where it stopped.
    /// </summary>
    public async Task<ServerMigrationReport> MigrateAsync(
        ICalendarEventRepository source, string dataDirectory, CancellationToken ct)
    {
        var all = source.FindAll();
        var events = all.Where(e => e.ExternalOrigin is null).ToList();
        var skipped = all.Count - events.Count;

        long? privateCalendarId = null;
        if (events.Any(IsPrivate))
            privateCalendarId = await PrivateCalendarIdAsync(ct).ConfigureAwait(false);

        var items = events.Select(e => ToImportItem(e, IsPrivate(e) ? privateCalendarId : null)).ToList();
        int created = 0, existing = 0;
        foreach (var batch in items.Chunk(_batchSize))
        {
            var response = await api.ImportEventsAsync(new EventImportRequestDto(batch), ct).ConfigureAwait(false);
            created += response.Results.Count(r => r.Created);
            existing += response.Results.Count(r => !r.Created);
        }

        var report = new ServerMigrationReport(
            items.Count, created, existing, events.Count(IsPrivate), skipped, clock.GetUtcNow(), api.BaseUri.AbsoluteUri);
        WriteMarker(dataDirectory, report);
        return report;
    }

    /// <summary>One local event as an item of the import request (the mapping table of decision 8).</summary>
    public static EventCreateDto ToImportItem(CalendarEvent ev, long? calendarId)
    {
        var body = ServerEventMapper.ToCreateDto(ev, calendarId) with
        {
            TimeZone = IanaTimeZone(ev.TimeZoneId.Value),
            Start = WholeMinute(ev.SingleSchedule?.StartUtc ?? ev.RecurringSchedule!.AnchorUtc),
        };
        if (!ev.IsRecurring()) return body with { ClientRef = ev.Id.Value };

        return body with
        {
            ClientRef = ev.Id.Value,
            // Overrides have no counterpart on the server (it splits an occurrence into its own event);
            // only skips are moved.
            Exceptions = [.. ev.Exceptions
                .Where(x => x.Type == ExceptionType.Skip)
                .Select(x => new EventExceptionDto(ServerEventMapper.ToKeyDto(x.OccurrenceKey), "SKIP"))],
            Moves = [.. ev.Moves.Select(m => ServerEventMapper.ToMoveDto(m, ev))],
        };
    }

    private static bool IsPrivate(CalendarEvent ev) => ev.Visibility is Visibility.Private or Visibility.Confidential;

    private async Task<long> PrivateCalendarIdAsync(CancellationToken ct)
    {
        var calendars = await api.ListCalendarsAsync(ct).ConfigureAwait(false);
        var existing = calendars
            .Where(c => c.Kind == "EVENTS" && c.Scope == "PRIVATE")
            .OrderByDescending(c => c.Name == PrivateCalendarName)
            .ThenBy(c => c.SortOrder)
            .FirstOrDefault();
        if (existing is not null) return existing.Id;

        var created = await api.CreateCalendarAsync(new CalendarCreateDto(PrivateCalendarName, "DEFAULT", "PRIVATE"), ct)
            .ConfigureAwait(false);
        return created.Id;
    }

    // Older events may carry a Windows zone id ("Tokyo Standard Time"); the server only knows IANA names.
    private static string IanaTimeZone(string id)
        => TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var iana) ? iana : id;

    // The server keys occurrences by wall-clock minute and refuses seconds.
    private static DateTimeOffset WholeMinute(DateTimeOffset instant)
        => new(instant.UtcTicks - instant.UtcTicks % TimeSpan.TicksPerMinute, TimeSpan.Zero);

    private static void WriteMarker(string dataDirectory, ServerMigrationReport report)
    {
        Directory.CreateDirectory(dataDirectory);
        var options = new JsonSerializerOptions(ServerJson.Options) { WriteIndented = true };
        File.WriteAllText(Path.Combine(dataDirectory, MarkerFileName), JsonSerializer.Serialize(report, options));
    }
}
