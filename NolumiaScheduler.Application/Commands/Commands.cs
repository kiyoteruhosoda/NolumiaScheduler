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
    EventAlarm? Alarm = null,
    EventColorKey ColorKey = EventColorKey.Default);

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
    EventAlarm? Alarm = null,
    EventColorKey ColorKey = EventColorKey.Default);

public sealed record UpdateEventCommand(
    string EventId,
    string Title,
    string? Location,
    Visibility Visibility,
    bool AllDay,
    DateOnly? NewDate,
    TimeSpan? NewStartTime,
    TimeSpan? NewEndTime,
    EventAlarm? Alarm,
    EventColorKey ColorKey = EventColorKey.Default);

public sealed record SkipOccurrenceCommand(
    string EventId,
    OccurrenceLocalKey OccurrenceKey);

/// <summary>
/// Splits a single occurrence out of a recurring series: the occurrence is excluded from the
/// series (skip) and a new standalone single event is created with the provided details.
/// This is the only supported way to edit a single occurrence of a recurring series.
/// </summary>
public sealed record SplitThisOccurrenceCommand(
    string EventId,
    OccurrenceLocalKey OccurrenceKey,
    string Title,
    string? Location,
    Visibility Visibility,
    bool AllDay,
    LocalDateValue NewDate,
    LocalTimeValue? StartTime,
    LocalTimeValue? EndTime,
    EventAlarm? Alarm = null,
    EventColorKey ColorKey = EventColorKey.Default);

public sealed record UpdateRecurringSeriesCommand(
    string EventId,
    string Title,
    string? Location,
    Visibility Visibility,
    bool AllDay,
    LocalTimeValue? StartTime,
    LocalTimeValue? EndTime,
    RecurrenceRule RecurrenceRule,
    EventAlarm? Alarm = null,
    EventColorKey ColorKey = EventColorKey.Default,
    LocalDateValue? NewStartDate = null);

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
    EventAlarm? Alarm = null,
    EventColorKey ColorKey = EventColorKey.Default,
    LocalDateValue? NewStartDate = null);

public sealed record DeleteFollowingOccurrencesCommand(
    string EventId,
    OccurrenceLocalKey FromOccurrenceKey);

/// <summary>
/// Moves a single occurrence of a recurring series to a new date/time (e.g. drag-and-drop).
/// Title, Location and Visibility override the series defaults when non-null; null keeps
/// the series value.
/// </summary>
public sealed record MoveOccurrenceCommand(
    string EventId,
    OccurrenceLocalKey OccurrenceKey,
    LocalDateValue NewDate,
    LocalTimeValue NewStartTime,
    LocalTimeValue NewEndTime,
    string? Title = null,
    string? Location = null,
    Visibility? Visibility = null);
