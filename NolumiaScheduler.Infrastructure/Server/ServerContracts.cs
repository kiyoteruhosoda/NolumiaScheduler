using System.Text.Json;
using System.Text.Json.Serialization;

namespace NolumiaScheduler.Infrastructure.Server;

// Wire shapes of the Scheduler server API (forge kyon/nolumiascheduler-server:
// src/presentation/api/schemas/calendar_schemas.py and calendar_set_schemas.py). Property names
// are converted to snake_case by ServerJson.Options. Instants are UTC ("Z"), dates "YYYY-MM-DD",
// wall times "HH:MM". Enums travel as their upper-case names ("WEEKLY", "MO", "TOMATO", …).

internal static class ServerJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false,
    };
}

// ── Events ─────────────────────────────────────────────────────────────────

public sealed record WeeklyRuleDto(IReadOnlyList<string> Weekdays);

public sealed record MonthlyRuleDto(string Kind, int? Day = null, int? WeekIndex = null, string? Weekday = null);

public sealed record YearlyRuleDto(string Kind, int Month, int? Day = null, int? WeekIndex = null, string? Weekday = null);

public sealed record AdjustmentRuleDto(string Condition, string ShiftUnit, int ShiftAmount, string Action = "SHIFT");

public sealed record RecurrenceRuleDto(
    string Type,
    int Interval,
    string? EndDate,
    WeeklyRuleDto? Weekly,
    MonthlyRuleDto? Monthly,
    YearlyRuleDto? Yearly,
    AdjustmentRuleDto? Adjustment);

// Spelled out: the snake_case policy would write "notify15_min", the server's name is "notify_15_min".
public sealed record AlarmDto(
    bool Enabled,
    [property: JsonPropertyName("notify_15_min")] bool Notify15Min,
    [property: JsonPropertyName("notify_5_min")] bool Notify5Min,
    [property: JsonPropertyName("notify_1_min")] bool Notify1Min,
    bool NotifyAtStart);

public sealed record OccurrenceKeyDto(string Date, string? StartTime);

public sealed record EventExceptionDto(OccurrenceKeyDto Occurrence, string Type);

public sealed record EventMoveDto(
    OccurrenceKeyDto Occurrence,
    string NewDate,
    string? NewStartTime,
    int? NewDurationMinutes,
    string? Title,
    string? Location);

/// <summary><c>CalendarEventResponse</c>. Also what <c>server-cache.json</c> keeps.</summary>
public sealed record EventDto(
    long Id,
    string Kind,
    string Title,
    string TimeZone,
    DateTimeOffset Start,
    int DurationMinutes,
    RecurrenceRuleDto? Recurrence,
    string? Location,
    string? Description,
    string ColorKey,
    long? TaskId,
    AlarmDto? Alarm,
    string EventType,
    long CalendarId,
    IReadOnlyList<EventExceptionDto> Exceptions,
    IReadOnlyList<EventMoveDto> Moves,
    int Version,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary><c>CalendarEventCreateRequest</c>; with <see cref="ClientRef"/> and the exceptions/moves it is one item of the import.</summary>
public sealed record EventCreateDto(
    string Title,
    string TimeZone,
    DateTimeOffset Start,
    int DurationMinutes,
    string? Location,
    string? Description,
    string ColorKey,
    long? TaskId,
    RecurrenceRuleDto? Recurrence,
    AlarmDto? Alarm,
    string EventType,
    long? CalendarId)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ClientRef { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<EventExceptionDto>? Exceptions { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<EventMoveDto>? Moves { get; init; }
}

/// <summary><c>CalendarEventUpdateRequest</c> (single events: details and, with start/duration, the time).</summary>
public sealed record EventUpdateDto(
    string Title,
    string? Location,
    string? Description,
    long? TaskId,
    DateTimeOffset Start,
    int DurationMinutes,
    string ColorKey,
    AlarmDto? Alarm,
    string EventType,
    long CalendarId,
    int? ExpectedVersion);

/// <summary><c>SeriesUpdateRequest</c> ("all occurrences" of a recurring event).</summary>
public sealed record SeriesUpdateDto(
    string Title,
    int DurationMinutes,
    RecurrenceRuleDto Recurrence,
    DateTimeOffset Start,
    string? Location,
    string? Description,
    long? TaskId,
    string ColorKey,
    AlarmDto? Alarm,
    string EventType,
    long CalendarId,
    int? ExpectedVersion);

public sealed record OccurrenceActionDto(OccurrenceKeyDto Occurrence, int? ExpectedVersion);

public sealed record OccurrenceMoveDto(
    OccurrenceKeyDto Occurrence,
    DateTimeOffset Start,
    int DurationMinutes,
    string? Title,
    string? Location,
    int? ExpectedVersion);

public sealed record EventImportRequestDto(IReadOnlyList<EventCreateDto> Events);

public sealed record EventImportResultDto(string ClientRef, long Id, bool Created);

public sealed record EventImportResponseDto(IReadOnlyList<EventImportResultDto> Results);

// ── Calendars, days off, imported occurrences ─────────────────────────────

public sealed record CalendarDto(
    long Id,
    string Kind,
    string Name,
    string ColorKey,
    int SortOrder,
    bool IsDefault,
    IReadOnlyList<string>? Workdays,
    string? DayOffReason,
    bool CountsAsDayOff,
    string Scope);

public sealed record CalendarCreateDto(string Name, string ColorKey, string Scope);

public sealed record FeedSourceDto(string Type);

public sealed record ImportedCalendarCreateDto(string Name, string ColorKey, string Scope, FeedSourceDto Source);

public sealed record DayOffMarkDto(string Date, string Reason, long? CalendarId, string? Name, bool CountsAsDayOff);

public sealed record ImportedOccurrenceDto(
    string Id,
    long CalendarId,
    string Title,
    string? Location,
    DateTimeOffset Start,
    int DurationMinutes,
    bool IsAllDay,
    bool IsPrivate = false,
    AlarmDto? Reminder = null,
    string? Origin = null);
