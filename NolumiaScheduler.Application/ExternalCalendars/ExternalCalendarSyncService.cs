using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.ExternalCalendars;
using NolumiaScheduler.Domain.Repositories;
using NolumiaScheduler.Domain.ValueObjects;
using Location = NolumiaScheduler.Domain.ValueObjects.Location;
using Visibility = NolumiaScheduler.Domain.ValueObjects.Visibility;

namespace NolumiaScheduler.Application.ExternalCalendars;

public sealed record ExternalSyncResult(bool SourceAvailable, int Added, int Updated, int Removed, int Unchanged);

/// <summary>
/// One-way reconciliation of an external calendar snapshot into the repository. Imported events
/// are matched by <see cref="ExternalOrigin.ExternalKey"/>; events no longer present in the
/// snapshot are deleted. Only changed events are saved to avoid needless change notifications.
/// </summary>
public sealed class ExternalCalendarSyncService(
    ICalendarEventRepository repository,
    TimeProvider clock,
    string timeZoneId)
{
    private const int MinutesPerDay = 24 * 60;

    private readonly ICalendarEventRepository _repository = repository;
    private readonly TimeProvider _clock = clock;
    private readonly TimeZoneId _timeZoneId = new(timeZoneId);

    public ExternalSyncResult Sync(IExternalCalendarSource source)
        => Apply(source.SourceId, source.ReadSnapshot());

    /// <summary>
    /// Reconciles an already-read snapshot (null = source unavailable, nothing changes).
    /// Lets callers read the file off the UI thread and apply on it.
    /// </summary>
    public ExternalSyncResult Apply(string sourceId, IReadOnlyList<ExternalAppointment>? snapshot)
    {
        if (snapshot == null) return new ExternalSyncResult(false, 0, 0, 0, 0);

        var existing = _repository.FindAll()
            .Where(e => e.ExternalOrigin?.SourceId == sourceId)
            .GroupBy(e => e.ExternalOrigin!.ExternalKey)
            .ToDictionary(g => g.Key, g => g.ToList());

        int added = 0, updated = 0, unchanged = 0, removed = 0;
        var seen = new HashSet<string>();
        var tz = _timeZoneId.ToTimeZoneInfo();

        foreach (var appt in snapshot)
        {
            if (!seen.Add(appt.Key)) continue;

            var origin = new ExternalOrigin(sourceId, appt.Key, appt.LastModified);
            var schedule = ToSchedule(appt, tz);
            var title = new EventTitle(string.IsNullOrWhiteSpace(appt.Title) ? "(no title)" : appt.Title);
            var location = string.IsNullOrWhiteSpace(appt.Location) ? null : new Location(appt.Location);
            var visibility = appt.IsPrivate ? Visibility.Private : Visibility.Public;

            if (existing.TryGetValue(appt.Key, out var matches))
            {
                var ev = matches[0];
                // Duplicates for one key can only come from a past glitch; drop the extras.
                foreach (var dup in matches.Skip(1))
                {
                    _repository.Delete(dup.Id);
                    removed++;
                }

                if (IsUpToDate(ev, origin, schedule, appt))
                {
                    unchanged++;
                    continue;
                }

                ev.ApplyExternalChanges(origin, title, location, visibility, schedule, ToAlarm(appt), _clock.GetUtcNow());
                _repository.Save(ev);
                updated++;
            }
            else
            {
                var ev = CalendarEvent.CreateExternalSingle(
                    new EventId(Guid.NewGuid().ToString()),
                    origin, title, location, visibility, _timeZoneId, schedule, ToAlarm(appt), _clock.GetUtcNow());
                _repository.Save(ev);
                added++;
            }
        }

        foreach (var (key, events) in existing)
        {
            if (seen.Contains(key)) continue;
            foreach (var ev in events)
            {
                _repository.Delete(ev.Id);
                removed++;
            }
        }

        return new ExternalSyncResult(true, added, updated, removed, unchanged);
    }

    // The alarm is not compared so that a user's per-event alarm toggles survive until the
    // appointment itself changes in the source.
    private static bool IsUpToDate(CalendarEvent ev, ExternalOrigin origin, SingleEventSchedule schedule, ExternalAppointment appt)
    {
        var s = ev.SingleSchedule;
        return s != null
            && Equals(ev.ExternalOrigin?.LastModified, origin.LastModified)
            && s.StartUtc == schedule.StartUtc
            && s.DurationMinutes == schedule.DurationMinutes
            && ev.Title.Value == (string.IsNullOrWhiteSpace(appt.Title) ? "(no title)" : appt.Title)
            && (ev.Location?.Value ?? "") == (string.IsNullOrWhiteSpace(appt.Location) ? "" : appt.Location);
    }

    internal static SingleEventSchedule ToSchedule(ExternalAppointment appt, TimeZoneInfo tz)
    {
        if (appt.IsAllDay)
        {
            // All-day dates are wall-calendar dates: anchor them at local midnight, never convert.
            var days = Math.Max(1, appt.AllDayEndExclusive.DayNumber - appt.AllDayStart.DayNumber);
            var startUtc = LocalSchedulePoint.StartInstant(
                LocalDateValue.FromDateOnly(appt.AllDayStart), new LocalTimeValue(0, 0, 0), tz).ToUniversalTime();
            return new SingleEventSchedule(startUtc, days * MinutesPerDay);
        }

        // Zero-length appointments (e.g. reminders/markers) are kept as 1-minute events.
        var minutes = (int)Math.Round((appt.EndUtc - appt.StartUtc).TotalMinutes);
        return new SingleEventSchedule(appt.StartUtc, Math.Max(1, minutes));
    }

    internal static EventAlarm ToAlarm(ExternalAppointment appt)
    {
        // All-day reminders (e.g. 420 min = previous evening) don't fit the fixed alarm buckets.
        if (appt.IsAllDay || appt.ReminderMinutes is not { } m || m < 0)
            return new EventAlarm(false, false, false, false, false);

        return new EventAlarm(true, m >= 15, m >= 5, m >= 1, true);
    }
}
