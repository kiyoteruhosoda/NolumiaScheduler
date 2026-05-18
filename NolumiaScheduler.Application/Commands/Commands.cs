using NolumiaScheduler.Domain.ValueObjects;
using Visibility = NolumiaScheduler.Domain.ValueObjects.Visibility;

namespace NolumiaScheduler.Application.Commands;

public sealed record CreateSingleEventCommand(
    string Title,
    string? Location,
    Visibility Visibility,
    string? EventType,
    string? Description,
    string TimeZone,
    bool AllDay,
    DateOnly StartDate,
    TimeSpan StartTime,
    TimeSpan EndTime,
    EventAlarm? Alarm = null);

public sealed record CreateRecurringEventCommand(
    string Title,
    string? Location,
    Visibility Visibility,
    string? EventType,
    string? Description,
    string TimeZone,
    bool AllDay,
    LocalDateValue StartDate,
    LocalTimeValue? StartTime,
    LocalTimeValue? EndTime,
    RecurrenceRule RecurrenceRule,
    EventAlarm? Alarm = null);

public sealed record UpdateEventCommand(
    string EventId,
    string Title,
    string? Location,
    Visibility Visibility,
    bool AllDay,
    DateOnly? NewDate,
    TimeSpan? NewStartTime,
    TimeSpan? NewEndTime,
    EventAlarm? Alarm);

public sealed record SkipOccurrenceCommand(
    string EventId,
    OccurrenceLocalKey OccurrenceKey);

public sealed record OverrideOccurrenceCommand(
    string EventId,
    OccurrenceLocalKey OccurrenceKey,
    string Title,
    string? Location,
    Visibility Visibility,
    bool AllDay,
    LocalDateValue Date,
    LocalTimeValue? StartTime,
    LocalTimeValue? EndTime);

public sealed record MoveOccurrenceCommand(
    string EventId,
    OccurrenceLocalKey OccurrenceKey,
    LocalDateValue NewDate,
    LocalTimeValue? NewStartTime,
    LocalTimeValue? NewEndTime,
    string? Title,
    string? Location,
    Visibility? Visibility);

public sealed record ChangeFollowingOccurrencesCommand(
    string EventId,
    OccurrenceLocalKey FromOccurrenceKey,
    string NewTitle,
    string? NewLocation,
    Visibility NewVisibility,
    bool NewAllDay,
    LocalTimeValue? NewStartTime,
    LocalTimeValue? NewEndTime,
    RecurrenceRule NewRecurrenceRule,
    EventAlarm? Alarm = null);
