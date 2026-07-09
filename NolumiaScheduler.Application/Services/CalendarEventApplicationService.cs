using NolumiaScheduler.Application.Commands;
using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.Exceptions;
using NolumiaScheduler.Domain.Repositories;
using NolumiaScheduler.Domain.ValueObjects;
using Visibility = NolumiaScheduler.Domain.ValueObjects.Visibility;
using Location = NolumiaScheduler.Domain.ValueObjects.Location;

namespace NolumiaScheduler.Application.Services;

public class CalendarEventApplicationService(
    ICalendarEventRepository repository,
    ICalendarEventChanges changes,
    TimeProvider clock)
{
    private readonly ICalendarEventRepository _repository = repository;
    private readonly ICalendarEventChanges _changes = changes;
    private readonly TimeProvider _clock = clock;

    public event Action? Changed
    {
        add => _changes.Changed += value;
        remove => _changes.Changed -= value;
    }

    public CalendarEvent? FindById(string eventId) =>
        _repository.FindById(new EventId(eventId));

    public IReadOnlyList<CalendarEvent> FindAll() =>
        _repository.FindAll();

    /// <summary>
    /// Events that may have an occurrence in the inclusive window [from, to]. A coarse
    /// pre-filter for calendar rendering; callers still expand occurrences exactly.
    /// </summary>
    public IReadOnlyList<CalendarEvent> FindByPeriod(LocalDateValue from, LocalDateValue to) =>
        _repository.FindByPeriod(from, to);

    public void CreateSingleEvent(CreateSingleEventCommand command)
    {
        var id = new EventId(Guid.NewGuid().ToString());
        var (startTime, duration) = ResolveTimes(command.StartTime, command.EndTime, command.AllDay);
        var startUtc = ToUtc(LocalDateValue.FromDateOnly(command.StartDate), startTime, command.TimeZone);
        var schedule = new SingleEventSchedule(startUtc, duration);

        var ev = CalendarEvent.CreateSingle(
            id,
            new EventTitle(command.Title),
            command.Location != null ? new Location(command.Location) : null,
            command.Visibility,
            command.EventType != null ? new EventType(command.EventType) : null,
            command.Description != null ? new Description(command.Description) : null,
            new TimeZoneId(command.TimeZone),
            schedule,
            _clock.GetUtcNow(),
            alarm: command.Alarm,
            colorKey: command.ColorKey);

        _repository.Save(ev);
    }

    public void CreateRecurringEvent(CreateRecurringEventCommand command)
    {
        var id = new EventId(Guid.NewGuid().ToString());
        var (startTime, duration) = ResolveTimes(command.StartTime, command.EndTime, command.AllDay);
        var anchorUtc = ToUtc(command.StartDate, startTime, command.TimeZone);
        var schedule = new RecurringEventSchedule(anchorUtc, duration, command.RecurrenceRule);

        var ev = CalendarEvent.CreateRecurring(
            id,
            new EventTitle(command.Title),
            command.Location != null ? new Location(command.Location) : null,
            command.Visibility,
            command.EventType != null ? new EventType(command.EventType) : null,
            command.Description != null ? new Description(command.Description) : null,
            new TimeZoneId(command.TimeZone),
            schedule,
            _clock.GetUtcNow(),
            alarm: command.Alarm,
            colorKey: command.ColorKey);

        _repository.Save(ev);
    }

    public void UpdateEvent(UpdateEventCommand command)
    {
        var ev = GetOrThrow(command.EventId);

        ev.ChangeDetails(
            new EventTitle(command.Title),
            command.Location != null ? new Location(command.Location) : null,
            command.Visibility,
            ev.EventType,
            command.Description != null ? new Description(command.Description) : null,
            _clock.GetUtcNow());

        var canReschedule = command.NewDate.HasValue && ev.IsSingle() &&
            (command.AllDay || (command.NewStartTime.HasValue && command.NewEndTime.HasValue));
        if (canReschedule)
        {
            var newDate = command.NewDate!.Value;
            var (startTime, duration) = ResolveTimes(
                command.NewStartTime ?? TimeSpan.Zero,
                command.NewEndTime ?? TimeSpan.Zero,
                command.AllDay);
            var startUtc = ToUtc(LocalDateValue.FromDateOnly(newDate), startTime, ev.TimeZoneId.Value);
            ev.RescheduleSingle(new SingleEventSchedule(startUtc, duration), _clock.GetUtcNow());
        }

        ev.SetAlarm(command.Alarm, _clock.GetUtcNow());
        ev.SetColor(command.ColorKey, _clock.GetUtcNow());
        _repository.Save(ev);
    }

    public void UpdateRecurringSeries(UpdateRecurringSeriesCommand command)
    {
        var ev = GetOrThrow(command.EventId);
        if (!ev.IsRecurring() || ev.RecurringSchedule == null)
            throw new DomainException("UpdateRecurringSeries is only valid for recurring events.");

        ev.ChangeDetails(
            new EventTitle(command.Title),
            command.Location != null ? new Location(command.Location) : null,
            command.Visibility,
            ev.EventType,
            command.Description != null ? new Description(command.Description) : null,
            _clock.GetUtcNow());

        // When opened from a specific occurrence the caller may pass a NewStartDate to move the
        // series anchor forward (e.g. editing the 6/30 occurrence via "Entire Series" moves the
        // anchor from 6/29 to 6/30, removing the earlier occurrence). Without NewStartDate the
        // original anchor is kept so existing exception/move keys remain aligned.
        var old = ev.RecurringSchedule;
        var (startTime, duration) = ResolveTimes(command.StartTime, command.EndTime, command.AllDay);
        var anchorLocalDate = command.NewStartDate
            ?? LocalSchedulePoint.LocalDateOf(old.AnchorUtc, ev.TimeZoneId.ToTimeZoneInfo());
        var anchorUtc = ToUtc(anchorLocalDate, startTime, ev.TimeZoneId.Value);
        var newSchedule = new RecurringEventSchedule(anchorUtc, duration, command.RecurrenceRule);

        ev.ChangeRecurrenceSchedule(newSchedule, _clock.GetUtcNow());
        ev.SetAlarm(command.Alarm, _clock.GetUtcNow());
        ev.SetColor(command.ColorKey, _clock.GetUtcNow());
        _repository.Save(ev);
    }

    /// <summary>
    /// Updates only the alarm settings of an event, leaving title, schedule and everything else
    /// untouched. Used by the alarm notification window's per-event offset toggles.
    /// </summary>
    public void SetEventAlarm(string eventId, EventAlarm alarm)
    {
        var ev = GetOrThrow(eventId);
        ev.SetAlarm(alarm, _clock.GetUtcNow());
        _repository.Save(ev);
    }

    public void SkipOccurrence(SkipOccurrenceCommand command)
    {
        var ev = GetOrThrow(command.EventId);
        ev.SkipOccurrence(command.OccurrenceKey, _clock.GetUtcNow());
        _repository.Save(ev);
    }

    /// <summary>
    /// Moves a single occurrence of a recurring series to a new date/time (e.g. drag-and-drop).
    /// </summary>
    public void MoveOccurrence(MoveOccurrenceCommand command)
    {
        var ev = GetOrThrow(command.EventId);
        if (!ev.IsRecurring())
            throw new DomainException("MoveOccurrence is only valid for recurring events.");

        var (startTime, duration) = ResolveTimes(command.NewStartTime, command.NewEndTime, false);
        ev.MoveOccurrence(
            command.OccurrenceKey,
            command.NewDate,
            startTime,
            duration,
            command.Title != null ? new EventTitle(command.Title) : null,
            command.Location != null ? new Location(command.Location) : null,
            command.Visibility,
            _clock.GetUtcNow());

        _repository.Save(ev);
    }

    /// <summary>
    /// Splits a single occurrence out of a recurring series: excludes the occurrence from the
    /// series (skip) and creates a new standalone single event with the given details.
    /// This is the only supported way to edit one occurrence of a recurring series.
    /// </summary>
    public void SplitThisOccurrence(SplitThisOccurrenceCommand command)
    {
        var ev = GetOrThrow(command.EventId);
        if (!ev.IsRecurring())
            throw new DomainException("SplitThisOccurrence is only valid for recurring events.");

        // Exclude the occurrence from the series
        ev.SkipOccurrence(command.OccurrenceKey, _clock.GetUtcNow());
        _repository.Save(ev);

        // Create a new standalone single event
        var newId = new EventId(Guid.NewGuid().ToString());
        var (startTime, duration) = ResolveTimes(command.StartTime, command.EndTime, command.AllDay);
        var startUtc = ToUtc(command.NewDate, startTime, ev.TimeZoneId.Value);
        var schedule = new SingleEventSchedule(startUtc, duration);

        var newEv = CalendarEvent.CreateSingle(
            newId,
            new EventTitle(command.Title),
            command.Location != null ? new Location(command.Location) : null,
            command.Visibility,
            ev.EventType,
            command.Description != null ? new Description(command.Description) : null,
            ev.TimeZoneId,
            schedule,
            _clock.GetUtcNow(),
            alarm: command.Alarm,
            colorKey: command.ColorKey);

        _repository.Save(newEv);
    }

    public void DeleteEvent(string eventId)
    {
        _repository.Delete(new EventId(eventId));
    }

    public void DeleteOccurrence(SkipOccurrenceCommand command)
    {
        SkipOccurrence(command);
    }

    public void ChangeFollowingOccurrences(ChangeFollowingOccurrencesCommand command)
    {
        var ev = GetOrThrow(command.EventId);
        if (!ev.IsRecurring() || ev.RecurringSchedule == null)
            throw new DomainException("ChangeFollowingOccurrences is only valid for recurring events.");

        // Splitting from the very first occurrence leaves the original series with no
        // occurrences, and an end date before its start date is invalid. In that case the edit
        // applies to the whole series, so drop the original rather than truncating it.
        var newEndDate = command.FromOccurrenceKey.Date.AddDays(-1);
        var seriesStartDate = LocalSchedulePoint.LocalDateOf(
            ev.RecurringSchedule.AnchorUtc, ev.TimeZoneId.ToTimeZoneInfo());
        if (newEndDate.CompareTo(seriesStartDate) < 0)
        {
            _repository.Delete(ev.Id);
        }
        else
        {
            ev.ChangeRecurrenceEndDate(newEndDate, _clock.GetUtcNow());
            _repository.Save(ev);
        }

        var newId = new EventId(Guid.NewGuid().ToString());
        var (newStartTime, newDuration) = ResolveTimes(command.NewStartTime, command.NewEndTime, command.NewAllDay);
        // NewStartDate lets the caller move the new series to a different date than the occurrence
        // being split on (e.g. "This and following" where the user also changed the start date).
        var newAnchorDate = command.NewStartDate ?? command.FromOccurrenceKey.Date;
        var newAnchorUtc = ToUtc(newAnchorDate, newStartTime, ev.TimeZoneId.Value);
        var newSchedule = new RecurringEventSchedule(newAnchorUtc, newDuration, command.NewRecurrenceRule);

        var newEv = CalendarEvent.CreateRecurring(
            newId,
            new EventTitle(command.NewTitle),
            command.NewLocation != null ? new Location(command.NewLocation) : null,
            command.NewVisibility,
            ev.EventType,
            command.Description != null ? new Description(command.Description) : null,
            ev.TimeZoneId,
            newSchedule,
            _clock.GetUtcNow(),
            alarm: command.Alarm,
            colorKey: command.ColorKey);

        _repository.Save(newEv);
    }

    public void DeleteFollowingOccurrences(DeleteFollowingOccurrencesCommand command)
    {
        var ev = GetOrThrow(command.EventId);
        if (!ev.IsRecurring() || ev.RecurringSchedule == null)
            throw new DomainException("DeleteFollowingOccurrences is only valid for recurring events.");

        var newEndDate = command.FromOccurrenceKey.Date.AddDays(-1);
        var seriesStartDate = LocalSchedulePoint.LocalDateOf(
            ev.RecurringSchedule.AnchorUtc, ev.TimeZoneId.ToTimeZoneInfo());
        if (newEndDate.CompareTo(seriesStartDate) < 0)
            _repository.Delete(ev.Id);
        else
        {
            ev.ChangeRecurrenceEndDate(newEndDate, _clock.GetUtcNow());
            _repository.Save(ev);
        }
    }

    private CalendarEvent GetOrThrow(string eventId)
    {
        var id = new EventId(eventId);
        return _repository.FindById(id)
            ?? throw new DomainException($"Event not found: {eventId}");
    }

    // ── Time → (start, duration) translation ─────────────────────────────────────────────
    // The UI contract still expresses a schedule as start/end times plus an all-day flag; the
    // domain stores start + duration (docs/time-model.md). These helpers bridge the two. All-day
    // maps to 00:00 + 24h. An end at or before the start is treated as crossing into the next day.

    private const int MinutesPerDay = 24 * 60;
    private static readonly LocalTimeValue Midnight = new(0, 0, 0);

    private static (LocalTimeValue startTime, int durationMinutes) ResolveTimes(
        TimeSpan startTime, TimeSpan endTime, bool allDay)
    {
        if (allDay) return (Midnight, MinutesPerDay);
        var start = LocalTimeValue.FromTimeOnly(TimeOnly.FromTimeSpan(startTime));
        var end = LocalTimeValue.FromTimeOnly(TimeOnly.FromTimeSpan(endTime));
        return (start, LocalSchedulePoint.WrappingDurationMinutes(start, end));
    }

    private static (LocalTimeValue startTime, int durationMinutes) ResolveTimes(
        LocalTimeValue? startTime, LocalTimeValue? endTime, bool allDay)
    {
        if (allDay || startTime == null || endTime == null) return (Midnight, MinutesPerDay);
        return (startTime, LocalSchedulePoint.WrappingDurationMinutes(startTime, endTime));
    }

    // Resolves a local wall-clock date+time in the given timezone to an absolute UTC instant
    // (docs/time-model.md §3): the stored value never re-resolves through the tz database again.
    private static DateTimeOffset ToUtc(LocalDateValue date, LocalTimeValue time, string timeZoneId)
        => LocalSchedulePoint.StartInstant(date, time, new TimeZoneId(timeZoneId).ToTimeZoneInfo())
            .ToUniversalTime();
}
