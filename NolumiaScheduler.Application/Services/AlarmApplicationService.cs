using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.Repositories;
using NolumiaScheduler.Domain.Services;
using NolumiaScheduler.Domain.ValueObjects;

namespace NolumiaScheduler.Application.Services;

/// <summary>
/// A notification that must be presented to the user now. <see cref="OffsetMinutes"/> is one of
/// <see cref="AlarmScheduleCalculator.Offsets"/> for regular alarms; snooze reminders carry the
/// original event data with <see cref="IsSnoozeReminder"/> set.
/// </summary>
public sealed record DueAlarm(
    string EventId,
    string Title,
    string? Location,
    int OffsetMinutes,
    bool IsSnoozeReminder,
    DateTime? OccurrenceStart);

public sealed record AlarmScheduleEntry(
    string EventId,
    string Title,
    DateTime OccurrenceStart,
    int MinutesBefore,
    DateTime NotifyAt,
    bool AlreadyFired,
    bool IsSnoozed);

/// <summary>
/// Owns the alarm state transitions (pending → fired → snoozed → re-fired / cancelled) for every
/// event, driven exclusively by the injected clock. The presentation layer hosts the timer and
/// shows notification windows; every decision about <em>whether</em> something is due lives here
/// so the date/time-driven transitions stay unit testable.
/// </summary>
public class AlarmApplicationService
{
    private readonly ICalendarEventRepository _repository;
    private readonly IOccurrenceExpander _expander;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private readonly HashSet<string> _firedKeys = [];
    private readonly List<SnoozeEntry> _snoozed = [];

    public AlarmApplicationService(
        ICalendarEventRepository repository,
        ICalendarEventChanges changes,
        IOccurrenceExpander expander,
        TimeProvider clock)
    {
        _repository = repository;
        _expander = expander;
        _clock = clock;

        // Any event change can invalidate the fired state (times may have moved), so start over.
        // Alarms whose notify time has long passed will not re-fire thanks to the catch-up grace.
        changes.Changed += ResetAllFiredKeys;
    }

    /// <summary>
    /// Returns every notification that became due since the last call and transitions it to the
    /// fired state (due snoozes are consumed). Callers present the result; calling again
    /// immediately yields nothing until the schedule or the clock moves on.
    /// </summary>
    public IReadOnlyList<DueAlarm> CollectDueAlarms()
    {
        var now = LocalNow();
        var due = new List<DueAlarm>();

        lock (_gate)
        {
            var dueSnoozes = _snoozed.Where(s => s.NotifyAt <= now).ToList();
            foreach (var snooze in dueSnoozes)
            {
                _snoozed.Remove(snooze);
                due.Add(new DueAlarm(
                    snooze.EventId, snooze.Title, snooze.Location,
                    OffsetMinutes: 0, IsSnoozeReminder: true, snooze.OccurrenceStart));
            }

            foreach (var planned in EnumeratePlannedAlarms(now))
            {
                if (_firedKeys.Contains(planned.Key)) continue;
                if (!AlarmScheduleCalculator.IsDue(now, planned.NotifyAt)) continue;

                _firedKeys.Add(planned.Key);
                due.Add(new DueAlarm(
                    planned.Event.Id.Value,
                    planned.Occurrence.Title.Value,
                    planned.Event.Location?.Value,
                    planned.OffsetMinutes,
                    IsSnoozeReminder: false,
                    planned.OccurrenceStart));
            }
        }

        return due;
    }

    /// <summary>Re-notify the alarm after the given delay from now.</summary>
    public void SnoozeFor(DueAlarm alarm, TimeSpan delay)
    {
        var notifyAt = LocalNow().Add(delay);
        lock (_gate)
            _snoozed.Add(new SnoozeEntry(alarm.EventId, alarm.Title, notifyAt, alarm.Location, alarm.OccurrenceStart));
    }

    /// <summary>
    /// Re-notify the alarm at the given lead time before the occurrence start. No-op when the
    /// alarm has no occurrence start time.
    /// </summary>
    public void SnoozeUntilBeforeStart(DueAlarm alarm, TimeSpan lead)
    {
        if (alarm.OccurrenceStart == null) return;

        var notifyAt = alarm.OccurrenceStart.Value - lead;
        lock (_gate)
            _snoozed.Add(new SnoozeEntry(alarm.EventId, alarm.Title, notifyAt, alarm.Location, alarm.OccurrenceStart));
    }

    /// <summary>
    /// Sets an explicit "next alarm" <paramref name="delay"/> from now. Only the offset alarms that
    /// would otherwise fire <em>before</em> it are skipped — see <see cref="SetNextAlarmAt"/>.
    /// </summary>
    public void SetNextAlarmFromNow(DueAlarm alarm, TimeSpan delay)
        => SetNextAlarmAt(alarm, LocalNow().Add(delay));

    /// <summary>
    /// Sets an explicit "next alarm" <paramref name="lead"/> before the occurrence start. Only the
    /// offset alarms that would otherwise fire <em>before</em> it are skipped (see
    /// <see cref="SetNextAlarmAt"/>). No-op when the alarm has no occurrence start time.
    /// </summary>
    public void SetNextAlarmBeforeStart(DueAlarm alarm, TimeSpan lead)
    {
        if (alarm.OccurrenceStart == null) return;
        SetNextAlarmAt(alarm, alarm.OccurrenceStart.Value - lead);
    }

    /// <summary>
    /// Makes <paramref name="notifyAt"/> the event's next alarm: it replaces any earlier explicit
    /// next-alarm and skips only the offset alarms that fall before it, so the explicit time is the
    /// next thing to fire while later offsets still ring. A short snooze that lands before the
    /// remaining offsets therefore keeps them; an explicit time past the event start skips them all.
    /// </summary>
    public void SetNextAlarmAt(DueAlarm alarm, DateTime notifyAt)
    {
        var now = LocalNow();
        lock (_gate)
        {
            foreach (var planned in EnumeratePlannedAlarms(now))
            {
                if (planned.Event.Id.Value == alarm.EventId && planned.NotifyAt < notifyAt)
                    _firedKeys.Add(planned.Key);
            }

            // The explicit next-alarm is single per event, so drop any earlier one before adding.
            _snoozed.RemoveAll(s => s.EventId == alarm.EventId);
            _snoozed.Add(new SnoozeEntry(
                alarm.EventId, alarm.Title, notifyAt, alarm.Location, alarm.OccurrenceStart));
        }
    }

    /// <summary>
    /// The soonest not-yet-fired alarm time of the same event <em>and the same occurrence</em>
    /// strictly after <paramref name="after"/>, or null. Scoping to the occurrence keeps a recurring
    /// reservation's "next alarm" within the current day — it never rolls over to the next
    /// occurrence. A pending snooze/explicit next-alarm for that occurrence counts too. When
    /// <paramref name="occurrenceStart"/> is null (an occurrence-less reminder) only the event is
    /// matched.
    /// </summary>
    public DateTime? GetNextAlarmTimeForOccurrence(string eventId, DateTime? occurrenceStart, DateTime after)
    {
        var now = LocalNow();
        DateTime? best = null;

        lock (_gate)
        {
            foreach (var planned in EnumeratePlannedAlarms(now))
            {
                if (planned.Event.Id.Value != eventId) continue;
                if (occurrenceStart != null && planned.OccurrenceStart != occurrenceStart.Value) continue;
                if (_firedKeys.Contains(planned.Key)) continue;
                if (planned.NotifyAt <= after) continue;
                if (best == null || planned.NotifyAt < best) best = planned.NotifyAt;
            }

            foreach (var snooze in _snoozed)
            {
                if (snooze.EventId != eventId) continue;
                if (occurrenceStart != null && snooze.OccurrenceStart != occurrenceStart) continue;
                if (snooze.NotifyAt <= after) continue;
                if (best == null || snooze.NotifyAt < best) best = snooze.NotifyAt;
            }
        }

        return best;
    }

    /// <summary>
    /// True when the event still has an alarm scheduled in the future (an unfired offset, or a
    /// pending snooze/explicit next-alarm). Used to hide "cancel remaining alarms" when there is
    /// nothing left to cancel.
    /// </summary>
    public bool HasRemainingAlarms(string eventId)
    {
        var now = LocalNow();
        lock (_gate)
        {
            if (_snoozed.Any(s => s.EventId == eventId && s.NotifyAt > now)) return true;

            foreach (var planned in EnumeratePlannedAlarms(now))
            {
                if (planned.Event.Id.Value == eventId
                    && !_firedKeys.Contains(planned.Key)
                    && planned.NotifyAt > now)
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// True when another alarm for the event is due <em>now</em> and has not been shown yet (its key
    /// is not fired, or a snooze for it is due). Because the alarm currently on screen is already
    /// marked fired, this signals that a newer alarm for the same event is waiting — the host uses it
    /// to auto-close the stale window.
    /// </summary>
    public bool HasUnshownDueAlarm(string eventId)
    {
        var now = LocalNow();
        lock (_gate)
        {
            if (_snoozed.Any(s => s.EventId == eventId && s.NotifyAt <= now)) return true;

            foreach (var planned in EnumeratePlannedAlarms(now))
            {
                if (planned.Event.Id.Value == eventId
                    && !_firedKeys.Contains(planned.Key)
                    && AlarmScheduleCalculator.IsDue(now, planned.NotifyAt))
                    return true;
            }
        }
        return false;
    }

    /// <summary>Marks every remaining notification of the event as fired and drops its snoozes.</summary>
    public void CancelRemainingAlarms(string eventId)
    {
        var now = LocalNow();
        lock (_gate)
        {
            foreach (var planned in EnumeratePlannedAlarms(now))
            {
                if (planned.Event.Id.Value == eventId)
                    _firedKeys.Add(planned.Key);
            }

            _snoozed.RemoveAll(s => s.EventId == eventId);
        }
    }

    public void ResetFiredKeys(string eventId)
    {
        lock (_gate)
            _firedKeys.RemoveWhere(k => k.StartsWith(eventId, StringComparison.Ordinal));
    }

    public void ResetAllFiredKeys()
    {
        lock (_gate)
            _firedKeys.Clear();
    }

    /// <summary>
    /// Re-adds previously fired keys. Used to take a snapshot, persist an unrelated change that
    /// resets all fired state (any repository write clears it), then restore so an alarm shown a
    /// moment ago does not immediately re-fire within the catch-up grace.
    /// </summary>
    public void RestoreFiredKeys(IEnumerable<string> keys)
    {
        lock (_gate)
            foreach (var key in keys)
                _firedKeys.Add(key);
    }

    public IReadOnlyList<AlarmScheduleEntry> GetScheduledAlarms()
    {
        var now = LocalNow();
        var results = new List<AlarmScheduleEntry>();

        lock (_gate)
        {
            foreach (var planned in EnumeratePlannedAlarms(now))
            {
                results.Add(new AlarmScheduleEntry(
                    planned.Event.Id.Value,
                    planned.Occurrence.Title.Value,
                    planned.OccurrenceStart,
                    planned.OffsetMinutes,
                    planned.NotifyAt,
                    _firedKeys.Contains(planned.Key),
                    IsSnoozed: false));
            }

            foreach (var snooze in _snoozed)
            {
                results.Add(new AlarmScheduleEntry(
                    snooze.EventId, snooze.Title, default, 0, snooze.NotifyAt,
                    AlreadyFired: false, IsSnoozed: true));
            }
        }

        return [.. results.OrderBy(e => e.NotifyAt)];
    }

    public IReadOnlyList<string> GetFiredKeys()
    {
        lock (_gate)
            return [.. _firedKeys];
    }

    public IReadOnlyList<string> GetDiagnosticLines()
    {
        var now = LocalNow();
        var today = new LocalDateValue(now.Year, now.Month, now.Day);
        var tomorrow = today.AddDays(1);

        var lines = new List<string>();
        var events = _repository.FindAll();
        lines.Add($"[Diag] Total events: {events.Count}, Range: {today} ~ {tomorrow}");

        foreach (var ev in events)
        {
            var evLabel = $"{ev.Title.Value} (id={ev.Id.Value[..Math.Min(8, ev.Id.Value.Length)]})";

            if (ev.Alarm == null)
            {
                lines.Add($"  SKIP {evLabel}: Alarm is null");
                continue;
            }
            if (!ev.Alarm.IsEnabled)
            {
                lines.Add($"  SKIP {evLabel}: Alarm disabled");
                continue;
            }

            var occurrences = _expander.Expand(ev, today, tomorrow, null);
            lines.Add($"  {evLabel}: Alarm ON, Occurrences in range = {occurrences.Count}");

            foreach (var occ in occurrences)
            {
                if (IsAllDay(occ))
                {
                    lines.Add($"    SKIP occ {occ.Date}: all-day (00:00 + 24h)");
                    continue;
                }
                if (!occ.AlarmEnabled)
                {
                    lines.Add($"    SKIP occ {occ.Date}: alarm silenced for this occurrence");
                    continue;
                }

                var occStart = ToOccurrenceStart(occ);
                lines.Add($"    occ {occ.Date} start={occStart:HH:mm} | 15m={ev.Alarm.Notify15Min} 5m={ev.Alarm.Notify5Min} 1m={ev.Alarm.Notify1Min} atStart={ev.Alarm.NotifyAtStart}");

                foreach (var min in AlarmScheduleCalculator.Offsets)
                {
                    if (!AlarmScheduleCalculator.IsOffsetEnabled(ev.Alarm, min)) continue;
                    var notifyAt = occStart.AddMinutes(-min);
                    var key = BuildKey(ev, occ, min);
                    bool fired;
                    lock (_gate)
                        fired = _firedKeys.Contains(key);
                    var inWindow = AlarmScheduleCalculator.IsDue(now, notifyAt);
                    lines.Add($"      {min}min: notifyAt={notifyAt:HH:mm:ss} fired={fired} inWindow={inWindow}");
                }
            }
        }

        return lines;
    }

    private DateTime LocalNow() => _clock.GetLocalNow().DateTime;

    private IEnumerable<PlannedAlarm> EnumeratePlannedAlarms(DateTime now)
    {
        var today = new LocalDateValue(now.Year, now.Month, now.Day);
        var tomorrow = today.AddDays(1);

        // Only events whose active span overlaps today..tomorrow can have a due alarm in
        // this window; the expander below still filters to exact occurrences.
        foreach (var ev in _repository.FindByPeriod(today, tomorrow))
        {
            if (ev.Alarm == null || !ev.Alarm.IsEnabled) continue;

            foreach (var occ in _expander.Expand(ev, today, tomorrow, null))
            {
                if (IsAllDay(occ) || !occ.AlarmEnabled) continue;

                var occStart = ToOccurrenceStart(occ);
                foreach (var min in AlarmScheduleCalculator.Offsets)
                {
                    if (!AlarmScheduleCalculator.IsOffsetEnabled(ev.Alarm, min)) continue;

                    yield return new PlannedAlarm(
                        ev, occ, occStart, min, BuildKey(ev, occ, min), occStart.AddMinutes(-min));
                }
            }
        }
    }

    // An all-day occurrence (start 00:00, full 24h) carries no meaningful start instant, so it
    // never raises an alarm — matching the original all-day behaviour.
    private static bool IsAllDay(EventOccurrence occ)
        => occ.StartMinuteOfDay == 0 && occ.DurationMinutes == 24 * 60;

    private static DateTime ToOccurrenceStart(EventOccurrence occ) => new(
        occ.Date.Year, occ.Date.Month, occ.Date.Day,
        occ.StartTime.Hour, occ.StartTime.Minute, occ.StartTime.Second);

    private static string BuildKey(CalendarEvent ev, EventOccurrence occ, int offsetMinutes)
        => $"{ev.Id.Value}:{occ.Date}:{offsetMinutes}";

    private sealed record PlannedAlarm(
        CalendarEvent Event,
        EventOccurrence Occurrence,
        DateTime OccurrenceStart,
        int OffsetMinutes,
        string Key,
        DateTime NotifyAt);

    private sealed record SnoozeEntry(
        string EventId,
        string Title,
        DateTime NotifyAt,
        string? Location,
        DateTime? OccurrenceStart);
}
