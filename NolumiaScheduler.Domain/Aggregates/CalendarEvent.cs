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

    public SingleEventSchedule? SingleSchedule { get; private set; }
    public RecurringEventSchedule? RecurringSchedule { get; private set; }

    private readonly List<EventException> _exceptions;
    public IReadOnlyList<EventException> Exceptions => _exceptions;

    private readonly List<EventMove> _moves;
    public IReadOnlyList<EventMove> Moves => _moves;

    private EventAlarm? _alarm;
    public EventAlarm? Alarm => _alarm;

    public EventColorKey ColorKey { get; private set; }

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
        SingleEventSchedule? singleSchedule,
        RecurringEventSchedule? recurringSchedule,
        DateTimeOffset createdAt,
        List<EventException>? exceptions = null,
        List<EventMove>? moves = null,
        VersionNo? version = null,
        EventAlarm? alarm = null,
        EventColorKey colorKey = EventColorKey.Default)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Kind = kind;
        Title = title ?? throw new ArgumentNullException(nameof(title));
        Location = location;
        Visibility = visibility;
        EventType = eventType;
        Description = description;
        TimeZoneId = timeZoneId ?? throw new ArgumentNullException(nameof(timeZoneId));
        SingleSchedule = singleSchedule;
        RecurringSchedule = recurringSchedule;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        _exceptions = exceptions ?? [];
        _moves = moves ?? [];
        Version = version ?? VersionNo.Initial();
        _alarm = alarm;
        ColorKey = colorKey;
    }

    public static CalendarEvent CreateSingle(
        EventId id,
        EventTitle title,
        Location? location,
        Visibility visibility,
        EventType? eventType,
        Description? description,
        TimeZoneId timeZoneId,
        SingleEventSchedule schedule,
        DateTimeOffset createdAt,
        EventAlarm? alarm = null,
        EventColorKey colorKey = EventColorKey.Default)
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
            singleSchedule: schedule,
            recurringSchedule: null,
            createdAt: createdAt,
            alarm: alarm,
            colorKey: colorKey);
    }

    public static CalendarEvent CreateRecurring(
        EventId id,
        EventTitle title,
        Location? location,
        Visibility visibility,
        EventType? eventType,
        Description? description,
        TimeZoneId timeZoneId,
        RecurringEventSchedule schedule,
        DateTimeOffset createdAt,
        EventAlarm? alarm = null,
        EventColorKey colorKey = EventColorKey.Default)
    {
        EnsureRecurrenceStartsOnOrBeforeEnd(timeZoneId, schedule);
        return new CalendarEvent(
            id: id,
            kind: EventKind.Recurring,
            title: title,
            location: location,
            visibility: visibility,
            eventType: eventType,
            description: description,
            timeZoneId: timeZoneId,
            singleSchedule: null,
            recurringSchedule: schedule,
            createdAt: createdAt,
            alarm: alarm,
            colorKey: colorKey);
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
        SingleEventSchedule? singleSchedule,
        RecurringEventSchedule? recurringSchedule,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        List<EventException> exceptions,
        List<EventMove> moves,
        VersionNo version,
        EventAlarm? alarm = null,
        EventColorKey colorKey = EventColorKey.Default)
    {
        var ev = new CalendarEvent(
            id, kind, title, location, visibility, eventType, description,
            timeZoneId, singleSchedule, recurringSchedule,
            createdAt, exceptions, moves, version, alarm, colorKey)
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

    public void SetColor(EventColorKey colorKey, DateTimeOffset updatedAt)
    {
        if (ColorKey == colorKey) return;
        ColorKey = colorKey;
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
        var newSchedule = RecurringSchedule.WithRecurrenceRule(newRule);
        EnsureRecurrenceStartsOnOrBeforeEnd(TimeZoneId, newSchedule);
        RecurringSchedule = newSchedule;

        Touch(updatedAt);
    }

    public void ChangeRecurrenceSchedule(
        RecurringEventSchedule newSchedule,
        DateTimeOffset updatedAt)
    {
        EnsureRecurringEvent();

        if (newSchedule == null)
            throw new DomainException("Recurring schedule is required.");

        EnsureRecurrenceStartsOnOrBeforeEnd(TimeZoneId, newSchedule);

        // Occurrence keys are (candidate date, series start time-of-day), so a start-time change
        // would orphan every existing skip/override/move. The nominal series time-of-day is the
        // anchor's local time in the event timezone; re-key when it changes.
        var tz = TimeZoneId.ToTimeZoneInfo();
        var oldStartTime = RecurringSchedule != null
            ? LocalSchedulePoint.LocalTimeOf(RecurringSchedule.AnchorUtc, tz) : null;
        var newStartTime = LocalSchedulePoint.LocalTimeOf(newSchedule.AnchorUtc, tz);
        if (!Equals(oldStartTime, newStartTime))
            RekeyOccurrenceCustomizations(oldStartTime, newStartTime);

        RecurringSchedule = newSchedule;
        Touch(updatedAt);
    }

    private void RekeyOccurrenceCustomizations(LocalTimeValue? oldTime, LocalTimeValue? newTime)
    {
        for (var i = 0; i < _exceptions.Count; i++)
        {
            var ex = _exceptions[i];
            if (!Equals(ex.OccurrenceKey.Time, oldTime)) continue;
            var key = new OccurrenceLocalKey(ex.OccurrenceKey.Date, newTime);
            _exceptions[i] = ex.Type == ExceptionType.Skip
                ? EventException.CreateSkip(key)
                : EventException.CreateOverride(key, ex.Override!);
        }

        for (var i = 0; i < _moves.Count; i++)
        {
            var move = _moves[i];
            if (!Equals(move.OccurrenceKey.Time, oldTime)) continue;
            _moves[i] = new EventMove(
                new OccurrenceLocalKey(move.OccurrenceKey.Date, newTime),
                move.NewDate, move.NewStartTime, move.NewDurationMinutes,
                move.Title, move.Location, move.Visibility);
        }
    }

    public void SkipOccurrence(
        OccurrenceLocalKey occurrenceKey,
        DateTimeOffset updatedAt)
    {
        EnsureRecurringEvent();
        RemoveExceptionInternal(occurrenceKey);

        _exceptions.Add(EventException.CreateSkip(occurrenceKey));
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

    public void MoveOccurrence(
        OccurrenceLocalKey occurrenceKey,
        LocalDateValue newDate,
        LocalTimeValue newStartTime,
        int durationMinutes,
        EventTitle? title,
        Location? location,
        Visibility? visibility,
        DateTimeOffset updatedAt)
    {
        EnsureRecurringEvent();

        var index = _moves.FindIndex(m => m.OccurrenceKey.Equals(occurrenceKey));
        if (index >= 0)
            _moves.RemoveAt(index);

        _moves.Add(new EventMove(occurrenceKey, newDate, newStartTime, durationMinutes, title, location, visibility));
        Touch(updatedAt);
    }

    /// <summary>
    /// Removes a drag-created move override for <paramref name="occurrenceKey"/>, restoring
    /// the occurrence to its canonical position in the series schedule.
    /// </summary>
    public void RemoveOccurrenceMove(OccurrenceLocalKey occurrenceKey, DateTimeOffset updatedAt)
    {
        EnsureRecurringEvent();
        var index = _moves.FindIndex(m => m.OccurrenceKey.Equals(occurrenceKey));
        if (index < 0)
            throw new DomainException("Occurrence move not found.");
        _moves.RemoveAt(index);
        Touch(updatedAt);
    }

    public bool IsSingle() => Kind == EventKind.Single;
    public bool IsRecurring() => Kind == EventKind.Recurring;

    public bool HasExceptionFor(OccurrenceLocalKey occurrenceKey)
        => _exceptions.Any(x => x.OccurrenceKey.Equals(occurrenceKey));

    /// <summary>
    /// Extra days added on each side of the active span when testing period overlap.
    /// A coarse filter must never drop an event that actually has an occurrence in the
    /// window; business-day adjustments can shift an occurrence a few days past the
    /// nominal span, so the margin keeps the filter conservative (false positives are
    /// harmless because the occurrence expander still filters exactly).
    /// </summary>
    public const int PeriodOverlapMarginDays = 31;

    /// <summary>
    /// The inclusive local-date range over which this event can produce occurrences:
    /// the single schedule's dates, or the recurrence start through its end date,
    /// widened to cover any relocated (moved) occurrences. This is a coarse bound, not
    /// an exact occurrence set.
    /// </summary>
    public (LocalDateValue Start, LocalDateValue End) GetActiveDateSpan()
    {
        var tz = TimeZoneId.ToTimeZoneInfo();

        if (IsSingle())
        {
            var single = SingleSchedule!;
            var start = LocalSchedulePoint.LocalDateOf(single.StartUtc, tz);
            var endLocalDt = TimeZoneInfo.ConvertTime(single.EndUtc, tz);
            // The end instant is exclusive; an occurrence that ends exactly at local midnight does
            // not extend the span into the following day.
            var endExclusive = DateOnly.FromDateTime(endLocalDt.DateTime);
            var endLast = endLocalDt.TimeOfDay == TimeSpan.Zero && endExclusive > start.ToDateOnly()
                ? endExclusive.AddDays(-1)
                : endExclusive;
            var end = LocalDateValue.FromDateOnly(endLast);
            return start <= end ? (start, end) : (end, start);
        }

        var schedule = RecurringSchedule!;
        var spanStart = LocalSchedulePoint.LocalDateOf(schedule.AnchorUtc, tz);
        var spanEnd = schedule.RecurrenceRule.EndDate;
        foreach (var move in _moves)
        {
            if (move.NewDate < spanStart) spanStart = move.NewDate;
            if (move.NewDate > spanEnd) spanEnd = move.NewDate;
        }
        return (spanStart, spanEnd);
    }

    /// <summary>
    /// Day-number bounds (<see cref="DateOnly.DayNumber"/>) of the active span, widened
    /// by <see cref="PeriodOverlapMarginDays"/>. Suitable for storing in an indexed
    /// column so a store can pre-filter by period without expanding occurrences.
    /// </summary>
    public (int StartDay, int EndDay) GetIndexedDaySpan()
    {
        var (start, end) = GetActiveDateSpan();
        return (start.ToDateOnly().DayNumber - PeriodOverlapMarginDays,
                end.ToDateOnly().DayNumber + PeriodOverlapMarginDays);
    }

    /// <summary>
    /// True when this event's active span (with margin) overlaps the inclusive window
    /// [<paramref name="from"/>, <paramref name="to"/>]. A coarse pre-filter for callers
    /// that then expand occurrences exactly.
    /// </summary>
    public bool OverlapsPeriod(LocalDateValue from, LocalDateValue to)
    {
        var (startDay, endDay) = GetIndexedDaySpan();
        return startDay <= to.ToDateOnly().DayNumber && endDay >= from.ToDateOnly().DayNumber;
    }

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

    // Aggregate invariant: the anchor's local date (resolved in the event's timezone) must fall on
    // or before the recurrence end date. It spans the schedule and the timezone, so it lives on the
    // aggregate root rather than the schedule value object.
    private static void EnsureRecurrenceStartsOnOrBeforeEnd(TimeZoneId timeZoneId, RecurringEventSchedule schedule)
    {
        var anchorLocalDate = LocalSchedulePoint.LocalDateOf(schedule.AnchorUtc, timeZoneId.ToTimeZoneInfo());
        if (anchorLocalDate.CompareTo(schedule.RecurrenceRule.EndDate) > 0)
            throw new DomainException("startDate must be on or before recurrence endDate.");
    }

    private bool RemoveExceptionInternal(OccurrenceLocalKey occurrenceKey)
    {
        var index = _exceptions.FindIndex(x => x.OccurrenceKey.Equals(occurrenceKey));
        if (index < 0) return false;
        _exceptions.RemoveAt(index);
        return true;
    }

    private void Touch(DateTimeOffset updatedAt)
    {
        UpdatedAt = updatedAt;
        Version = Version.Next();
    }
}
