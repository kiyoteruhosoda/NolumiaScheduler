using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.Repositories;
using NolumiaScheduler.Domain.Services;

namespace NolumiaScheduler.Application.Services;

/// <summary>
/// Deletes events whose final occurrence has already ended, so fired events do not accumulate
/// in the store forever. A retention window keeps just-finished events around briefly: the
/// calendar can still show what happened recently, snoozed alarms that straddle midnight
/// survive, and a business-day shift landing slightly past the series end is never purged
/// while still upcoming.
/// </summary>
public class PurgeExpiredEventsService(
    ICalendarEventRepository eventRepository,
    IBusinessCalendarRepository businessCalendarRepository,
    IEventExpirationService expirationService,
    TimeProvider clock)
{
    public static readonly TimeSpan DefaultRetention = TimeSpan.FromDays(1);

    private readonly ICalendarEventRepository _eventRepository = eventRepository;
    private readonly IBusinessCalendarRepository _businessCalendarRepository = businessCalendarRepository;
    private readonly IEventExpirationService _expirationService = expirationService;
    private readonly TimeProvider _clock = clock;

    /// <summary>
    /// Deletes every event that expired before now minus <paramref name="retention"/>
    /// (default <see cref="DefaultRetention"/>). Returns the number of deleted events.
    /// </summary>
    public int PurgeExpiredEvents(TimeSpan? retention = null)
    {
        var cutoff = _clock.GetUtcNow() - (retention ?? DefaultRetention);

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
