using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.Entities;
using NolumiaScheduler.Domain.Repositories;
using NolumiaScheduler.Domain.ValueObjects;
using NolumiaScheduler.Infrastructure.Json.Repositories;
using NolumiaScheduler.Infrastructure.Sqlite.Db;
using NolumiaScheduler.Infrastructure.Sqlite.Repositories;
using DomainLocation = NolumiaScheduler.Domain.ValueObjects.Location;
using DomainVisibility = NolumiaScheduler.Domain.ValueObjects.Visibility;

namespace NolumiaScheduler.Infrastructure.Migration;

/// <summary>
/// One-shot migration of calendar events from the legacy start/end + all-day schema to the
/// current DTSTART+TZID+duration model (docs/time-model.md §8). Legacy records are parsed into
/// domain objects and re-saved through the normal repositories, so the new JSON shape — and the
/// SQLite span columns — are produced by the same code the app uses. Business calendars and app
/// settings are unaffected (their schema did not change).
/// </summary>
public static class LegacySchemaMigrator
{
    public sealed record Report(int Migrated, int AlreadyCurrent, int Failed);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>Migrates every legacy JSON event file under <paramref name="eventsDirectory"/> in place.</summary>
    public static Report MigrateJsonEvents(string eventsDirectory, bool dryRun = false, TextWriter? log = null)
    {
        if (!Directory.Exists(eventsDirectory))
            return new Report(0, 0, 0);

        var repo = new JsonCalendarEventRepository(eventsDirectory);
        int migrated = 0, current = 0, failed = 0;

        foreach (var file in Directory.GetFiles(eventsDirectory, "*.json"))
        {
            var raw = File.ReadAllText(file);
            try
            {
                if (!IsLegacy(raw)) { current++; continue; }
                var ev = ParseLegacy(raw);
                if (!dryRun) repo.Save(ev);
                migrated++;
                log?.WriteLine($"  {(dryRun ? "would migrate" : "migrated")} {Path.GetFileName(file)}");
            }
            catch (Exception ex)
            {
                failed++;
                log?.WriteLine($"  FAILED {Path.GetFileName(file)}: {ex.Message}");
            }
        }

        return new Report(migrated, current, failed);
    }

    /// <summary>Migrates every legacy event row in the SQLite database in place (payload + span).</summary>
    public static Report MigrateSqliteEvents(SqliteConnectionFactory factory, bool dryRun = false, TextWriter? log = null)
    {
        var rows = new List<(string Id, string Payload)>();
        using (var connection = factory.Open())
        {
            using var select = connection.CreateCommand();
            select.CommandText = "SELECT id, payload FROM calendar_events;";
            using var reader = select.ExecuteReader();
            while (reader.Read())
                rows.Add((reader.GetString(0), reader.GetString(1)));
        }

        var repo = new SqliteCalendarEventRepository(factory);
        int migrated = 0, current = 0, failed = 0;

        foreach (var (id, payload) in rows)
        {
            try
            {
                if (!IsLegacy(payload)) { current++; continue; }
                var ev = ParseLegacy(payload);
                if (!dryRun) repo.Save(ev); // upsert by id → rewrites payload and recomputes the span columns
                migrated++;
                log?.WriteLine($"  {(dryRun ? "would migrate" : "migrated")} {id}");
            }
            catch (Exception ex)
            {
                failed++;
                log?.WriteLine($"  FAILED {id}: {ex.Message}");
            }
        }

        return new Report(migrated, current, failed);
    }

    // A record needs migration when its schedule is not yet in the UTC form, i.e. the single
    // schedule lacks "startUtc" (or the recurring schedule lacks "anchorUtc"). This covers both the
    // original (start/end + allDay) and the interim (startDate/startTime/durationMinutes) shapes.
    private static bool IsLegacy(string rawJson)
    {
        using var doc = JsonDocument.Parse(rawJson);
        var root = doc.RootElement;
        if (root.TryGetProperty("singleSchedule", out var ss) && ss.ValueKind == JsonValueKind.Object)
            return !ss.TryGetProperty("startUtc", out _);
        if (root.TryGetProperty("recurringSchedule", out var rs) && rs.ValueKind == JsonValueKind.Object)
            return !rs.TryGetProperty("anchorUtc", out _);
        return false;
    }

    private static CalendarEvent ParseLegacy(string rawJson)
    {
        var dto = JsonSerializer.Deserialize<LegacyEventDto>(rawJson, Json)
            ?? throw new InvalidOperationException("Could not parse legacy event JSON.");

        var kind = Enum.Parse<EventKind>(dto.Kind);
        var visibility = Enum.Parse<DomainVisibility>(dto.Visibility);
        var tz = new TimeZoneId(dto.TimeZoneId);
        var timeZone = tz.ToTimeZoneInfo();

        SingleEventSchedule? single = null;
        RecurringEventSchedule? recurring = null;

        if (kind == EventKind.Single && dto.SingleSchedule is { } ss)
        {
            DateTimeOffset startUtc;
            int duration;
            if (!string.IsNullOrEmpty(ss.Start))
            {
                // Original shape: absolute start/end.
                var start = DateTimeOffset.Parse(ss.Start, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                var end = DateTimeOffset.Parse(ss.End!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                startUtc = start.ToUniversalTime();
                duration = (int)Math.Round((end - start).TotalMinutes);
            }
            else
            {
                // Interim shape: local startDate/startTime + durationMinutes.
                startUtc = LocalStartToUtc(ss.StartDate!, ss.StartTime!, timeZone);
                duration = ss.DurationMinutes ?? 0;
            }
            single = new SingleEventSchedule(startUtc, duration <= 0 ? 24 * 60 : duration);
        }
        else if (kind == EventKind.Recurring && dto.RecurringSchedule is { } rs)
        {
            int duration;
            LocalTimeValue startTime;
            if (rs.DurationMinutes is { } dm && string.IsNullOrEmpty(rs.EndTime))
            {
                // Interim shape: startDate/startTime + durationMinutes.
                startTime = ParseTime(rs.StartTime!);
                duration = dm;
            }
            else
            {
                (startTime, duration) = ResolveTimes(dto.AllDay, rs.StartTime, rs.EndTime);
            }
            var anchorUtc = LocalStartToUtc(rs.StartDate, startTime, timeZone);
            recurring = new RecurringEventSchedule(anchorUtc, duration, rs.Rule.ToDomain());
        }

        var exceptions = (dto.Exceptions ?? []).Select(e => ToException(e, dto.AllDay)).ToList();
        var moves = (dto.Moves ?? []).Select(m => ToMove(m, dto.AllDay)).ToList();

        EventAlarm? alarm = dto.Alarm is { } a
            ? new EventAlarm(a.IsEnabled, a.Notify15Min, a.Notify5Min, a.Notify1Min, a.NotifyAtStart)
            : null;

        var colorKey = dto.Color != null && Enum.TryParse<EventColorKey>(dto.Color, out var c)
            ? c : EventColorKey.Default;

        return CalendarEvent.Reconstitute(
            new EventId(dto.Id), kind, new EventTitle(dto.Title),
            string.IsNullOrEmpty(dto.Location) ? null : new DomainLocation(dto.Location),
            visibility,
            string.IsNullOrEmpty(dto.EventType) ? null : new EventType(dto.EventType),
            string.IsNullOrEmpty(dto.Description) ? null : new Description(dto.Description),
            tz, single, recurring,
            DateTimeOffset.Parse(dto.CreatedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            DateTimeOffset.Parse(dto.UpdatedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            exceptions, moves, new VersionNo(dto.Version <= 0 ? 1 : dto.Version),
            alarm: alarm, colorKey: colorKey);
    }

    private static EventException ToException(LegacyExceptionDto dto, bool allDay)
    {
        var key = new OccurrenceLocalKey(ParseDate(dto.Date), dto.Time != null ? ParseTime(dto.Time) : null);
        if (string.Equals(dto.Type, "Skip", StringComparison.OrdinalIgnoreCase) || dto.Override is null)
            return EventException.CreateSkip(key);

        var ov = dto.Override;
        var (startTime, duration) = ResolveOptionalTimes(allDay, ov.StartTime, ov.EndTime);
        return EventException.CreateOverride(key, new ExceptionOverride(
            title: string.IsNullOrEmpty(ov.Title) ? null : new EventTitle(ov.Title),
            location: string.IsNullOrEmpty(ov.Location) ? null : new DomainLocation(ov.Location),
            visibility: string.IsNullOrEmpty(ov.Visibility) ? null : Enum.Parse<DomainVisibility>(ov.Visibility),
            startTime: startTime,
            durationMinutes: duration));
    }

    private static EventMove ToMove(LegacyMoveDto dto, bool allDay)
    {
        var key = new OccurrenceLocalKey(ParseDate(dto.Date), dto.Time != null ? ParseTime(dto.Time) : null);
        var (startTime, duration) = ResolveOptionalTimes(allDay, dto.NewStartTime, dto.NewEndTime);
        return new EventMove(
            key, ParseDate(dto.NewDate), startTime, duration,
            string.IsNullOrEmpty(dto.Title) ? null : new EventTitle(dto.Title),
            string.IsNullOrEmpty(dto.Location) ? null : new DomainLocation(dto.Location),
            string.IsNullOrEmpty(dto.Visibility) ? null : Enum.Parse<DomainVisibility>(dto.Visibility));
    }

    private static (LocalTimeValue startTime, int durationMinutes) ResolveTimes(bool allDay, string? startStr, string? endStr)
    {
        if (allDay || string.IsNullOrEmpty(startStr) || string.IsNullOrEmpty(endStr))
            return (new LocalTimeValue(0, 0, 0), 24 * 60);
        var start = ParseTime(startStr);
        return (start, LocalSchedulePoint.WrappingDurationMinutes(start, ParseTime(endStr)));
    }

    private static (LocalTimeValue? startTime, int? durationMinutes) ResolveOptionalTimes(bool allDay, string? startStr, string? endStr)
    {
        if (allDay || string.IsNullOrEmpty(startStr) || string.IsNullOrEmpty(endStr))
            return (null, null);
        var start = ParseTime(startStr);
        return (start, LocalSchedulePoint.WrappingDurationMinutes(start, ParseTime(endStr)));
    }

    private static DateTimeOffset LocalStartToUtc(string dateStr, string timeStr, TimeZoneInfo tz)
        => LocalSchedulePoint.StartInstant(ParseDate(dateStr), ParseTime(timeStr), tz).ToUniversalTime();

    private static DateTimeOffset LocalStartToUtc(string dateStr, LocalTimeValue time, TimeZoneInfo tz)
        => LocalSchedulePoint.StartInstant(ParseDate(dateStr), time, tz).ToUniversalTime();

    private static LocalDateValue ParseDate(string s)
    {
        var d = DateOnly.Parse(s, CultureInfo.InvariantCulture);
        return new LocalDateValue(d.Year, d.Month, d.Day);
    }

    private static LocalTimeValue ParseTime(string s)
    {
        var t = TimeOnly.Parse(s, CultureInfo.InvariantCulture);
        return new LocalTimeValue(t.Hour, t.Minute, t.Second);
    }

    // ── Legacy DTOs (pre-refactor on-disk shape; camelCase via Web options) ──────────────────

    private sealed class LegacyEventDto
    {
        public string Id { get; set; } = "";
        public string Kind { get; set; } = "";
        public string Title { get; set; } = "";
        public string? Location { get; set; }
        public string Visibility { get; set; } = "Public";
        public string? EventType { get; set; }
        public string? Description { get; set; }
        public string TimeZoneId { get; set; } = "";
        public bool AllDay { get; set; }
        public LegacySingleScheduleDto? SingleSchedule { get; set; }
        public LegacyRecurringScheduleDto? RecurringSchedule { get; set; }
        public List<LegacyExceptionDto>? Exceptions { get; set; }
        public List<LegacyMoveDto>? Moves { get; set; }
        public LegacyAlarmDto? Alarm { get; set; }
        public string? Color { get; set; }
        public int Version { get; set; }
        public string CreatedAt { get; set; } = "";
        public string UpdatedAt { get; set; } = "";
    }

    private sealed class LegacySingleScheduleDto
    {
        // Original shape
        public string? Start { get; set; }
        public string? End { get; set; }
        // Interim shape
        public string? StartDate { get; set; }
        public string? StartTime { get; set; }
        public int? DurationMinutes { get; set; }
    }

    private sealed class LegacyRecurringScheduleDto
    {
        public string StartDate { get; set; } = "";
        public string? StartTime { get; set; }
        public string? EndTime { get; set; }       // original shape
        public int? DurationMinutes { get; set; }  // interim shape
        public RecurrenceRuleDto Rule { get; set; } = new();
    }

    private sealed class LegacyExceptionDto
    {
        public string Date { get; set; } = "";
        public string? Time { get; set; }
        public string Type { get; set; } = "";
        public LegacyOverrideDto? Override { get; set; }
    }

    private sealed class LegacyOverrideDto
    {
        public string? Title { get; set; }
        public string? Location { get; set; }
        public string? Visibility { get; set; }
        public string? StartTime { get; set; }
        public string? EndTime { get; set; }
    }

    private sealed class LegacyMoveDto
    {
        public string Date { get; set; } = "";
        public string? Time { get; set; }
        public string NewDate { get; set; } = "";
        public string? NewStartTime { get; set; }
        public string? NewEndTime { get; set; }
        public string? Title { get; set; }
        public string? Location { get; set; }
        public string? Visibility { get; set; }
    }

    private sealed class LegacyAlarmDto
    {
        public bool IsEnabled { get; set; } = true;
        public bool Notify15Min { get; set; } = true;
        public bool Notify5Min { get; set; } = true;
        public bool Notify1Min { get; set; } = true;
        public bool NotifyAtStart { get; set; } = true;
    }
}
