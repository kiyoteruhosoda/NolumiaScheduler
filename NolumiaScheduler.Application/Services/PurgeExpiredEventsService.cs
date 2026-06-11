using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.Repositories;
using NolumiaScheduler.Domain.Services;

namespace NolumiaScheduler.Application.Services;

/// <summary>
/// Deletes events whose final occurrence ended before the start of the current local day, so
/// fired events do not accumulate in the store. Today's already-finished events are kept until
/// the date rolls over: the calendar still shows what happened earlier today, and snoozes or
/// business-day shifts landing later the same day are never cut off.
/// </summary>
public class PurgeExpiredEventsService(
    ICalendarEventRepository eventRepository,
    IBusinessCalendarRepository businessCalendarRepository,
    IEventExpirationService expirationService,
    TimeProvider clock)
{
    private readonly ICalendarEventRepository _eventRepository = eventRepository;
    private readonly IBusinessCalendarRepository _businessCalendarRepository = businessCalendarRepository;
    private readonly IEventExpirationService _expirationService = expirationService;
    private readonly TimeProvider _clock = clock;

    /// <summary>
    /// Deletes every event whose final occurrence ended before today (local midnight).
    /// Returns the number of deleted events.
    /// </summary>
    public int PurgeExpiredEvents()
    {
        var localNow = _clock.GetLocalNow();
        var cutoff = new DateTimeOffset(localNow.Date, localNow.Offset);

        var purged = 0;
        foreach (var calendarEvent in _eventRepository.FindAll())
        {
            var businessCalendar = ResolveBusinessCalendar(calendarEvent);
            if (!_expirationService.IsExpired(calendarEvent, businessCalendar, cutoff))
                continue;

            _eventRepository.Delete(calendarEvent.Id);
            purged++;
        }

        return purged;
    }

    private BusinessCalendar? ResolveBusinessCalendar(CalendarEvent calendarEvent)
    {
        var calendarId = calendarEvent.RecurringSchedule?.RecurrenceRule.Adjustment?.CalendarId;
        return calendarId != null ? _businessCalendarRepository.FindById(calendarId) : null;
    }
}
