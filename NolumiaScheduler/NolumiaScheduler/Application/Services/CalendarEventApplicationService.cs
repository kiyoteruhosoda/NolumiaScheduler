using NolumiaScheduler.Application.Commands;
using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.Entities;
using NolumiaScheduler.Domain.Exceptions;
using NolumiaScheduler.Domain.Repositories;
using NolumiaScheduler.Domain.ValueObjects;
using Visibility = NolumiaScheduler.Domain.ValueObjects.Visibility;
using Location = NolumiaScheduler.Domain.ValueObjects.Location;

namespace NolumiaScheduler.Application.Services;

public class CalendarEventApplicationService
{
    private readonly ICalendarEventRepository _repository;

    public CalendarEventApplicationService(ICalendarEventRepository repository)
    {
        _repository = repository;
    }

    public CalendarEvent CreateSingleEvent(CreateSingleEventCommand command)
    {
        var id = new EventId(Guid.NewGuid().ToString());
        var schedule = new SingleEventSchedule(command.Start, command.End);

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
            DateTimeOffset.UtcNow);

        _repository.Save(ev);
        return ev;
    }

    public CalendarEvent CreateRecurringEvent(CreateRecurringEventCommand command)
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
            DateTimeOffset.UtcNow);

        _repository.Save(ev);
        return ev;
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

    public CalendarEvent ChangeFollowingOccurrences(ChangeFollowingOccurrencesCommand command)
    {
        var ev = GetOrThrow(command.EventId);

        // 1. 既存イベントの endDate を変更前最終回の前日に切る
        var newEndDate = command.FromOccurrenceKey.Date.AddDays(-1);
        ev.ChangeRecurrenceEndDate(newEndDate, DateTimeOffset.UtcNow);
        _repository.Save(ev);

        // 2. 新しい繰り返しイベントを作成
        var newId = new EventId(Guid.NewGuid().ToString());
        var newSchedule = new RecurringEventSchedule(
            command.FromOccurrenceKey.Date,
            command.NewStartTime ?? ev.RecurringSchedule!.StartTime,
            command.NewEndTime ?? ev.RecurringSchedule!.EndTime,
            command.NewRecurrenceRule,
            ev.AllDay);

        var newEv = CalendarEvent.CreateRecurring(
            newId,
            ev.Title,
            ev.Location,
            ev.Visibility,
            ev.EventType,
            ev.Description,
            ev.TimeZoneId,
            ev.AllDay,
            newSchedule,
            DateTimeOffset.UtcNow);

        _repository.Save(newEv);
        return newEv;
    }

    private CalendarEvent GetOrThrow(string eventId)
    {
        var id = new EventId(eventId);
        return _repository.FindById(id)
            ?? throw new DomainException($"Event not found: {eventId}");
    }

    public void DeleteEvent(string eventId)
    {
        _repository.Delete(new EventId(eventId));
    }

    public void DeleteOccurrence(SkipOccurrenceCommand command)
    {
        SkipOccurrence(command);
    }
}
