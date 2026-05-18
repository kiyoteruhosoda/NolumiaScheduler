using NolumiaScheduler.Application.Commands;
using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.Entities;
using NolumiaScheduler.Domain.Exceptions;
using NolumiaScheduler.Domain.Repositories;
using NolumiaScheduler.Domain.ValueObjects;
using Visibility = NolumiaScheduler.Domain.ValueObjects.Visibility;
using Location = NolumiaScheduler.Domain.ValueObjects.Location;

namespace NolumiaScheduler.Application.Services;

public class CalendarEventApplicationService(ICalendarEventRepository repository, ICalendarEventChanges changes)
{
    private readonly ICalendarEventRepository _repository = repository;
    private readonly ICalendarEventChanges _changes = changes;

    public event Action? Changed
    {
        add => _changes.Changed += value;
        remove => _changes.Changed -= value;
    }

    public CalendarEvent? FindById(string eventId) =>
        _repository.FindById(new EventId(eventId));

    public IReadOnlyList<CalendarEvent> FindAll() =>
        _repository.FindAll();

    public void CreateSingleEvent(CreateSingleEventCommand command)
    {
        var id = new EventId(Guid.NewGuid().ToString());
        var (start, end) = ResolveSchedule(
            command.StartDate, command.StartTime,
            command.StartDate, command.EndTime,
            command.AllDay, command.TimeZone);
        var schedule = new SingleEventSchedule(start, end);

        var ev = CalendarEvent.CreateSingle(
            id,
            new EventTitle(command.Title),
            command.Location != null ? new Location(command.Location) : null,
            command.Visibility,
            command.EventType != null ? new EventType(command.EventType) : null,
            command.Description != null ? new Description(command.Description) : null,
            new TimeZoneId(command.TimeZone),
            command.AllDay,
            schedule,
            DateTimeOffset.UtcNow,
            alarm: command.Alarm);

        _repository.Save(ev);
    }

    public void CreateRecurringEvent(CreateRecurringEventCommand command)
    {
        var id = new EventId(Guid.NewGuid().ToString());
        var schedule = new RecurringEventSchedule(
            command.StartDate,
            command.StartTime,
            command.EndTime,
            command.RecurrenceRule,
            command.AllDay);

        var ev = CalendarEvent.CreateRecurring(
            id,
            new EventTitle(command.Title),
            command.Location != null ? new Location(command.Location) : null,
            command.Visibility,
            command.EventType != null ? new EventType(command.EventType) : null,
            command.Description != null ? new Description(command.Description) : null,
            new TimeZoneId(command.TimeZone),
            command.AllDay,
            schedule,
            DateTimeOffset.UtcNow,
            alarm: command.Alarm);

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
            ev.Description,
            DateTimeOffset.UtcNow);

        var canReschedule = command.NewDate.HasValue && ev.IsSingle() &&
            (command.AllDay || (command.NewStartTime.HasValue && command.NewEndTime.HasValue));
        if (canReschedule)
        {
            var (start, end) = ResolveSchedule(
                command.NewDate.Value, command.NewStartTime ?? TimeSpan.Zero,
                command.NewDate.Value, command.NewEndTime ?? TimeSpan.Zero,
                command.AllDay, ev.TimeZoneId.Value);
            ev.RescheduleSingle(new SingleEventSchedule(start, end), DateTimeOffset.UtcNow);
        }

        ev.SetAlarm(command.Alarm, DateTimeOffset.UtcNow);
        _repository.Save(ev);
    }

    public void SkipOccurrence(SkipOccurrenceCommand command)
    {
        var ev = GetOrThrow(command.EventId);
        ev.SkipOccurrence(command.OccurrenceKey, DateTimeOffset.UtcNow);
        _repository.Save(ev);
    }

    public void OverrideOccurrence(OverrideOccurrenceCommand command)
    {
        var ev = GetOrThrow(command.EventId);
        if (!ev.IsRecurring())
            throw new DomainException("OverrideOccurrence is only valid for recurring events.");

        var exceptionOverride = new ExceptionOverride(
            title: new EventTitle(command.Title),
            location: command.Location != null ? new Location(command.Location) : null,
            visibility: command.Visibility,
            startTime: command.AllDay ? null : command.StartTime,
            endTime: command.AllDay ? null : command.EndTime);

        ev.OverrideOccurrence(command.OccurrenceKey, exceptionOverride, DateTimeOffset.UtcNow);

        if (!command.OccurrenceKey.Date.Equals(command.Date))
        {
            var move = new EventMove(
                command.OccurrenceKey,
                command.Date,
                command.AllDay ? null : command.StartTime,
                command.AllDay ? null : command.EndTime,
                new EventTitle(command.Title),
                command.Location != null ? new Location(command.Location) : null,
                command.Visibility);
            ev.MoveOccurrence(move, DateTimeOffset.UtcNow);
        }

        _repository.Save(ev);
    }

    public void MoveOccurrence(MoveOccurrenceCommand command)
    {
        var ev = GetOrThrow(command.EventId);

        var move = new EventMove(
            command.OccurrenceKey,
            command.NewDate,
            command.NewStartTime,
            command.NewEndTime,
            command.Title != null ? new EventTitle(command.Title) : null,
            command.Location != null ? new Location(command.Location) : null,
            command.Visibility);

        ev.MoveOccurrence(move, DateTimeOffset.UtcNow);
        _repository.Save(ev);
    }

    public void ChangeFollowingOccurrences(ChangeFollowingOccurrencesCommand command)
    {
        var ev = GetOrThrow(command.EventId);
        if (!ev.IsRecurring() || ev.RecurringSchedule == null)
            throw new DomainException("ChangeFollowingOccurrences is only valid for recurring events.");

        var newEndDate = command.FromOccurrenceKey.Date.AddDays(-1);
        ev.ChangeRecurrenceEndDate(newEndDate, DateTimeOffset.UtcNow);
        _repository.Save(ev);

        var newId = new EventId(Guid.NewGuid().ToString());
        var newSchedule = new RecurringEventSchedule(
            command.FromOccurrenceKey.Date,
            command.NewAllDay ? null : command.NewStartTime,
            command.NewAllDay ? null : command.NewEndTime,
            command.NewRecurrenceRule,
            command.NewAllDay);

        var newEv = CalendarEvent.CreateRecurring(
            newId,
            new EventTitle(command.NewTitle),
            command.NewLocation != null ? new Location(command.NewLocation) : null,
            command.NewVisibility,
            ev.EventType,
            ev.Description,
            ev.TimeZoneId,
            command.NewAllDay,
            newSchedule,
            DateTimeOffset.UtcNow,
            alarm: command.Alarm);

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

    private CalendarEvent GetOrThrow(string eventId)
    {
        var id = new EventId(eventId);
        return _repository.FindById(id)
            ?? throw new DomainException($"Event not found: {eventId}");
    }

    private static (DateTimeOffset start, DateTimeOffset end) ResolveSchedule(
        DateOnly startDate, TimeSpan startTime,
        DateOnly endDate, TimeSpan endTime,
        bool allDay, string timeZoneId)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        if (allDay)
        {
            var dt = startDate.ToDateTime(TimeOnly.MinValue);
            var offset = tz.GetUtcOffset(dt);
            var start = new DateTimeOffset(dt, offset);
            return (start, start.AddDays(1));
        }
        var startDt = startDate.ToDateTime(TimeOnly.FromTimeSpan(startTime));
        var endDt   = endDate.ToDateTime(TimeOnly.FromTimeSpan(endTime));
        var off     = tz.GetUtcOffset(startDt);
        return (new DateTimeOffset(startDt, off), new DateTimeOffset(endDt, off));
    }
}
