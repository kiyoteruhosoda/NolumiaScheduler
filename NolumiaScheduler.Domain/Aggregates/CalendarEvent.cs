using NolumiaScheduler.Domain.Entities;
using NolumiaScheduler.Domain.Exceptions;
using NolumiaScheduler.Domain.ValueObjects;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NolumiaScheduler.Domain.Aggregates;

public class CalendarEvent
{
    public EventId Id { get; }
    public EventKind Kind { get; }
    public EventTitle Title { get; private set; }
    public Location? Location { get; private set; }
    public Visibility Visibility { get; private set; }
    public EventType? EventType { get; private set; }
    public Description? Description { get; private set; }
    public TimeZoneId TimeZoneId { get; }

    public bool AllDay { get; }

    public SingleEventSchedule? SingleSchedule { get; private set; }
    public RecurringEventSchedule? RecurringSchedule { get; private set; }

    private readonly List<EventException> _exceptions;
    public IReadOnlyList<EventException> Exceptions => _exceptions;

    private readonly List<EventMove> _moves;
    public IReadOnlyList<EventMove> Moves => _moves;

    private EventAlarm? _alarm;
    public EventAlarm? Alarm => _alarm;

    public VersionNo Version { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private CalendarEvent(
        EventId id,
        EventKind kind,
        EventTitle title,
        Location? location,
        Visibility visibility,
        EventType? eventType,
        Description? description,
        TimeZoneId timeZoneId,
        bool allDay,
        SingleEventSchedule? singleSchedule,
        RecurringEventSchedule? recurringSchedule,
        DateTimeOffset createdAt,
        List<EventException>? exceptions = null,
        List<EventMove>? moves = null,
        VersionNo? version = null,
        EventAlarm? alarm = null)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Kind = kind;
        Title = title ?? throw new ArgumentNullException(nameof(title));
        Location = location;
        Visibility = visibility;
        EventType = eventType;
        Description = description;
        TimeZoneId = timeZoneId ?? throw new ArgumentNullException(nameof(timeZoneId));
        AllDay = allDay;
        SingleSchedule = singleSchedule;
        RecurringSchedule = recurringSchedule;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        _exceptions = exceptions ?? [];
        _moves = moves ?? [];
        Version = version ?? VersionNo.Initial();
        _alarm = alarm;
    }

    public static CalendarEvent CreateSingle(
        EventId id,
        EventTitle title,
        Location? location,
        Visibility visibility,
        EventType? eventType,
        Description? description,
        TimeZoneId timeZoneId,
        bool allDay,
        SingleEventSchedule schedule,
        DateTimeOffset createdAt,
        EventAlarm? alarm = null)
    {
        return new CalendarEvent(
            id: id,
            kind: EventKind.Single,
            title: title,
            location: location,
            visibility: visibility,
            eventType: eventType,
            description: description,
            timeZoneId: timeZoneId,
            allDay: allDay,
            singleSchedule: schedule,
            recurringSchedule: null,
            createdAt: createdAt,
            alarm: alarm);
    }

    public static CalendarEvent CreateRecurring(
        EventId id,
        EventTitle title,
        Location? location,
        Visibility visibility,
        EventType? eventType,
        Description? description,
        TimeZoneId timeZoneId,
        bool allDay,
        RecurringEventSchedule schedule,
        DateTimeOffset createdAt,
        EventAlarm? alarm = null)
    {
        return new CalendarEvent(
            id: id,
            kind: EventKind.Recurring,
            title: title,
            location: location,
            visibility: visibility,
            eventType: eventType,
            description: description,
            timeZoneId: timeZoneId,
            allDay: allDay,
            singleSchedule: null,
            recurringSchedule: schedule,
            createdAt: createdAt,
            alarm: alarm);
    }

    public static CalendarEvent Reconstitute(
        EventId id,
        EventKind kind,
        EventTitle title,
        Location? location,
        Visibility visibility,
        EventType? eventType,
        Description? description,
        TimeZoneId timeZoneId,
        bool allDay,
        SingleEventSchedule? singleSchedule,
        RecurringEventSchedule? recurringSchedule,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        List<EventException> exceptions,
        List<EventMove> moves,
        VersionNo version,
        EventAlarm? alarm = null)
    {
        var ev = new CalendarEvent(
            id, kind, title, location, visibility, eventType, description,
            timeZoneId, allDay, singleSchedule, recurringSchedule,
            createdAt, exceptions, moves, version, alarm)
        {
            UpdatedAt = updatedAt
        };
        return ev;
    }

    public void SetAlarm(EventAlarm? alarm, DateTimeOffset updatedAt)
    {
        _alarm = alarm;
        Touch(updatedAt);
    }

    public void ChangeDetails(
        EventTitle title,
        Location? location,
        Visibility visibility,
        EventType? eventType,
        Description? description,
        DateTimeOffset updatedAt)
    {
        Title = title;
        Location = location;
        Visibility = visibility;
        EventType = eventType;
        Description = description;
        Touch(updatedAt);
    }

    public void RescheduleSingle(
        SingleEventSchedule newSchedule,
        DateTimeOffset updatedAt)
    {
        EnsureSingleEvent();
        SingleSchedule = newSchedule;
        Touch(updatedAt);
    }

    public void ChangeRecurrenceEndDate(
        LocalDateValue newEndDate,
        DateTimeOffset updatedAt)
    {
        EnsureRecurringEvent();

        if (RecurringSchedule == null)
            throw new DomainException("Recurring schedule is required.");

        var newRule = RecurringSchedule.RecurrenceRule.WithEndDate(newEndDate);
        RecurringSchedule = RecurringSchedule.WithRecurrenceRule(newRule);

        Touch(updatedAt);
    }

    public void SkipOccurrence(
        OccurrenceLocalKey occurrenceKey,
        DateTimeOffset updatedAt)
    {
        EnsureRecurringEvent();
        EnsureOccurrenceKeyNotMoved(occurrenceKey);
        RemoveExceptionInternal(occurrenceKey);

        _exceptions.Add(EventException.CreateSkip(occurrenceKey));
        Touch(updatedAt);
    }

    public void OverrideOccurrence(
        OccurrenceLocalKey occurrenceKey,
        ExceptionOverride exceptionOverride,
        DateTimeOffset updatedAt)
    {
        EnsureRecurringEvent();
        EnsureOccurrenceKeyNotMoved(occurrenceKey);
        EnsureOverrideCompatible(exceptionOverride);

        RemoveExceptionInternal(occurrenceKey);
        _exceptions.Add(EventException.CreateOverride(occurrenceKey, exceptionOverride));
        Touch(updatedAt);
    }

    public void MoveOccurrence(
        EventMove move,
        DateTimeOffset updatedAt)
    {
        EnsureRecurringEvent();
        EnsureOccurrenceKeyNotExcepted(move.OccurrenceKey);
        EnsureMoveCompatible(move);

        RemoveMoveInternal(move.OccurrenceKey);
        _moves.Add(move);
        Touch(updatedAt);
    }

    public void RemoveOccurrenceException(
        OccurrenceLocalKey occurrenceKey,
        DateTimeOffset updatedAt)
    {
        EnsureRecurringEvent();
        var removed = RemoveExceptionInternal(occurrenceKey);
        if (!removed)
            throw new DomainException("Occurrence exception not found.");
        Touch(updatedAt);
    }

    public void RemoveOccurrenceMove(
        OccurrenceLocalKey occurrenceKey,
        DateTimeOffset updatedAt)
    {
        EnsureRecurringEvent();
        var removed = RemoveMoveInternal(occurrenceKey);
        if (!removed)
            throw new DomainException("Occurrence move not found.");
        Touch(updatedAt);
    }

    public bool IsSingle() => Kind == EventKind.Single;
    public bool IsRecurring() => Kind == EventKind.Recurring;

    public bool HasExceptionFor(OccurrenceLocalKey occurrenceKey)
        => _exceptions.Any(x => x.OccurrenceKey.Equals(occurrenceKey));

    public bool HasMoveFor(OccurrenceLocalKey occurrenceKey)
        => _moves.Any(x => x.OccurrenceKey.Equals(occurrenceKey));

    public bool HasOccurrenceCustomization(OccurrenceLocalKey occurrenceKey)
        => HasExceptionFor(occurrenceKey) || HasMoveFor(occurrenceKey);

    private void EnsureSingleEvent()
    {
        if (Kind != EventKind.Single)
            throw new DomainException("This operation is only allowed for single events.");
    }

    private void EnsureRecurringEvent()
    {
        if (Kind != EventKind.Recurring)
            throw new DomainException("This operation is only allowed for recurring events.");
    }

    private void EnsureOccurrenceKeyNotMoved(OccurrenceLocalKey occurrenceKey)
    {
        if (_moves.Any(x => x.OccurrenceKey.Equals(occurrenceKey)))
            throw new DomainException("The occurrence is already moved.");
    }

    private void EnsureOccurrenceKeyNotExcepted(OccurrenceLocalKey occurrenceKey)
    {
        if (_exceptions.Any(x => x.OccurrenceKey.Equals(occurrenceKey)))
            throw new DomainException("The occurrence already has an exception.");
    }

    private void EnsureOverrideCompatible(ExceptionOverride exceptionOverride)
    {
        if (AllDay)
        {
            if (exceptionOverride.StartTime != null || exceptionOverride.EndTime != null)
                throw new DomainException("All-day recurring event cannot override times.");
        }
        else
        {
            var hasStart = exceptionOverride.StartTime != null;
            var hasEnd = exceptionOverride.EndTime != null;

            if (hasStart != hasEnd)
                throw new DomainException("Override start/end time must be specified together.");

            if (hasStart && exceptionOverride.StartTime!.CompareTo(exceptionOverride.EndTime!) >= 0)
                throw new DomainException("Override start time must be before end time.");
        }

        if (exceptionOverride.IsEmpty())
            throw new DomainException("Override payload must not be empty.");
    }

    private void EnsureMoveCompatible(EventMove move)
    {
        if (AllDay)
        {
            if (move.NewStartTime != null || move.NewEndTime != null)
                throw new DomainException("All-day recurring event move must not have times.");
        }
        else
        {
            if (move.NewStartTime == null || move.NewEndTime == null)
                throw new DomainException("Timed recurring event move requires times.");

            if (move.NewStartTime.CompareTo(move.NewEndTime) >= 0)
                throw new DomainException("Move start time must be before end time.");
        }
    }

    private bool RemoveExceptionInternal(OccurrenceLocalKey occurrenceKey)
    {
        var index = _exceptions.FindIndex(x => x.OccurrenceKey.Equals(occurrenceKey));
        if (index < 0) return false;
        _exceptions.RemoveAt(index);
        return true;
    }

    private bool RemoveMoveInternal(OccurrenceLocalKey occurrenceKey)
    {
        var index = _moves.FindIndex(x => x.OccurrenceKey.Equals(occurrenceKey));
        if (index < 0) return false;
        _moves.RemoveAt(index);
        return true;
    }

    private void Touch(DateTimeOffset updatedAt)
    {
        UpdatedAt = updatedAt;
        Version = Version.Next();
    }
}
