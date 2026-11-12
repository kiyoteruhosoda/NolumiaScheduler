using NolumiaScheduler.Application.Services;
using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.Entities;
using NolumiaScheduler.Domain.Services;
using NolumiaScheduler.Domain.ValueObjects;
using Visibility = NolumiaScheduler.Domain.ValueObjects.Visibility;

namespace NolumiaScheduler.CoreTests;

/// <summary>
/// Time-driven alarm state transitions (pending → due/fired → snoozed → re-fired → cancelled),
/// exercised through a fake clock. The clock's local time zone is UTC so event times equal
/// wall-clock times in the assertions.
/// </summary>
[TestClass]
public class AlarmApplicationServiceTests
{
    // Event starts 2026-06-10 10:00 UTC; default alarm offsets are 15/5/1/0 minutes before.
    private static readonly DateTimeOffset EventStart = new(2026, 6, 10, 10, 0, 0, TimeSpan.Zero);

    private InMemoryCalendarEventRepository _repo = null!;
    private InMemoryBusinessCalendarRepository _calendarRepo = null!;
    private FakeClock _clock = null!;
    private AlarmApplicationService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _repo = new InMemoryCalendarEventRepository();
        _calendarRepo = new InMemoryBusinessCalendarRepository();
        _clock = new FakeClock(EventStart.AddHours(-1));
        _service = new AlarmApplicationService(
            _repo, _repo, _calendarRepo, new OccurrenceExpander(new BusinessDayShiftService()), _clock);
    }

    // ── pending → due ──────────────────────────────────────────────────────

    [TestMethod]
    public void BeforeNotificationTime_NothingFires()
    {
        SaveEvent("ev");
        _clock.SetUtcNow(EventStart.AddMinutes(-16));

        Assert.HasCount(0, _service.CollectDueAlarms());
        Assert.HasCount(0, _service.GetFiredKeys());
    }

    [TestMethod]
    public void AtNotificationTime_FiresAndBecomesFired()
    {
        SaveEvent("ev");
        _clock.SetUtcNow(EventStart.AddMinutes(-15));

        var due = _service.CollectDueAlarms();

        Assert.HasCount(1, due);
        Assert.AreEqual("ev", due[0].EventId);
        Assert.AreEqual(15, due[0].OffsetMinutes);
        Assert.IsFalse(due[0].IsSnoozeReminder);
        Assert.AreEqual(EventStart.DateTime, due[0].OccurrenceStart);
    }

    [TestMethod]
    public void FiredAlarm_CollectedAgainAtSameTime_DoesNotFire()
    {
        SaveEvent("ev");
        _clock.SetUtcNow(EventStart.AddMinutes(-15));

        Assert.HasCount(1, _service.CollectDueAlarms());
        Assert.HasCount(0, _service.CollectDueAlarms(), "The second time is empty because it already fired");
    }

    [TestMethod]
    public void OverTime_Advances15Min_5Min_1Min_StartTimeInOrder()
    {
        SaveEvent("ev");

        _clock.SetUtcNow(EventStart.AddMinutes(-15));
        Assert.AreEqual(15, _service.CollectDueAlarms().Single().OffsetMinutes);

        _clock.SetUtcNow(EventStart.AddMinutes(-5));
        Assert.AreEqual(5, _service.CollectDueAlarms().Single().OffsetMinutes);

        _clock.SetUtcNow(EventStart.AddMinutes(-1));
        Assert.AreEqual(1, _service.CollectDueAlarms().Single().OffsetMinutes);

        _clock.SetUtcNow(EventStart);
        Assert.AreEqual(0, _service.CollectDueAlarms().Single().OffsetMinutes);

        _clock.SetUtcNow(EventStart.AddMinutes(2));
        Assert.HasCount(0, _service.CollectDueAlarms(), "Nothing is left after all offsets are consumed");
    }

    [TestMethod]
    public void NotificationPastGrace_IsSkippedWithoutFiring()
    {
        SaveEvent("ev");
        // 15-min notify time passed 2 minutes ago — beyond the catch-up grace (1 min).
        _clock.SetUtcNow(EventStart.AddMinutes(-13));

        Assert.HasCount(0, _service.CollectDueAlarms());
    }

    [TestMethod]
    public void DisabledAlarm_DoesNotFire()
    {
        SaveEvent("ev", new EventAlarm(IsEnabled: false, true, true, true, true));
        _clock.SetUtcNow(EventStart.AddMinutes(-15));

        Assert.HasCount(0, _service.CollectDueAlarms());
    }

    [TestMethod]
    public void PerOffsetSettings_OnlyEnabledOnesFire()
    {
        SaveEvent("ev", new EventAlarm(true, Notify15Min: false, Notify5Min: true, Notify1Min: false, NotifyAtStart: false));

        _clock.SetUtcNow(EventStart.AddMinutes(-15));
        Assert.HasCount(0, _service.CollectDueAlarms());

        _clock.SetUtcNow(EventStart.AddMinutes(-5));
        Assert.AreEqual(5, _service.CollectDueAlarms().Single().OffsetMinutes);
    }

    // ── fired → snoozed → re-fired ─────────────────────────────────────────

    [TestMethod]
    public void Snooze_RenotifiesAfterGivenTime()
    {
        SaveEvent("ev");
        _clock.SetUtcNow(EventStart.AddMinutes(-15));
        var due = _service.CollectDueAlarms().Single();

        _service.SnoozeFor(due, TimeSpan.FromMinutes(5));

        _clock.Advance(TimeSpan.FromMinutes(4));
        Assert.HasCount(0, _service.CollectDueAlarms(), "No re-notification before the snooze expires");

        _clock.Advance(TimeSpan.FromMinutes(1));
        var reminder = _service.CollectDueAlarms();
        Assert.HasCount(1, reminder.Where(a => a.IsSnoozeReminder).ToList());
        Assert.AreEqual("ev", reminder.First(a => a.IsSnoozeReminder).EventId);
    }

    [TestMethod]
    public void SnoozeUntil5MinBeforeStart_RenotifiesAt5MinBeforeStart()
    {
        SaveEvent("ev");
        _clock.SetUtcNow(EventStart.AddMinutes(-15));
        var due = _service.CollectDueAlarms().Single();

        _service.SnoozeUntilBeforeStart(due, TimeSpan.FromMinutes(5));

        _clock.SetUtcNow(EventStart.AddMinutes(-6));
        Assert.HasCount(0, _service.CollectDueAlarms().Where(a => a.IsSnoozeReminder).ToList());

        _clock.SetUtcNow(EventStart.AddMinutes(-5));
        var due2 = _service.CollectDueAlarms();
        // The regular 5-min alarm and the snooze reminder both become due at this instant.
        Assert.IsTrue(due2.Any(a => a.IsSnoozeReminder));
        Assert.IsTrue(due2.Any(a => !a.IsSnoozeReminder && a.OffsetMinutes == 5));
    }

    [TestMethod]
    public void SnoozeBeforeStart_WithoutStartTime_IsIgnored()
    {
        var orphan = new DueAlarm("ev", "title", null, 0, true, OccurrenceStart: null);

        _service.SnoozeUntilBeforeStart(orphan, TimeSpan.FromMinutes(5));

        Assert.HasCount(0, _service.GetScheduledAlarms().Where(e => e.IsSnoozed).ToList());
    }

    // ── next-alarm override (explicit time wins over offsets) ───────────────

    [TestMethod]
    public void SetNextAlert_FromNow_SuppressesAllOffsetsAndRenotifiesOnceAtGivenTime()
    {
        SaveEvent("ev");
        _clock.SetUtcNow(EventStart.AddMinutes(-15));
        var due = _service.CollectDueAlarms().Single();

        // Re-notification time = (EventStart-15) + 20 = EventStart+5
        _service.SetNextAlarmFromNow(due, TimeSpan.FromMinutes(20));

        _clock.SetUtcNow(EventStart.AddMinutes(-5));
        Assert.HasCount(0, _service.CollectDueAlarms(), "The 5-minute offset is suppressed");

        _clock.SetUtcNow(EventStart);
        Assert.HasCount(0, _service.CollectDueAlarms(), "The start-time offset is suppressed too");

        _clock.SetUtcNow(EventStart.AddMinutes(5));
        var reminder = _service.CollectDueAlarms();
        Assert.HasCount(1, reminder);
        Assert.IsTrue(reminder[0].IsSnoozeReminder);
        Assert.AreEqual("ev", reminder[0].EventId);
    }

    [TestMethod]
    public void SetNextAlert_SuppressesOnlyOffsetsBeforeGivenTimeAndKeepsLaterOnes()
    {
        SaveEvent("ev");
        _clock.SetUtcNow(EventStart.AddMinutes(-15));
        var due = _service.CollectDueAlarms().Single();

        // Next alert = 3 minutes before start. The earlier 5-minute offset is suppressed; the later 1-minute and start-time offsets remain.
        _service.SetNextAlarmBeforeStart(due, TimeSpan.FromMinutes(3));

        _clock.SetUtcNow(EventStart.AddMinutes(-5));
        Assert.HasCount(0, _service.CollectDueAlarms(), "The 5-minute offset before the given time is suppressed");

        _clock.SetUtcNow(EventStart.AddMinutes(-3));
        Assert.IsTrue(_service.CollectDueAlarms().Any(a => a.IsSnoozeReminder), "The explicit alert rings 3 minutes before start");

        _clock.SetUtcNow(EventStart.AddMinutes(-1));
        Assert.AreEqual(1, _service.CollectDueAlarms().Single().OffsetMinutes, "The 1-minute offset after the given time remains");

        _clock.SetUtcNow(EventStart);
        Assert.AreEqual(0, _service.CollectDueAlarms().Single().OffsetMinutes, "The start time remains too");
    }

    [TestMethod]
    public void SnoozeBeforeRemainingOffsets_5Min1MinAndStartTimeStillFire()
    {
        SaveEvent("ev");
        _clock.SetUtcNow(EventStart.AddMinutes(-15));
        var due = _service.CollectDueAlarms().Single(); // 15-minute alert

        // 5 minutes from now = 10 minutes before start. The remaining 5-min/1-min/start-time offsets are all later, so they are not marked fired.
        _service.SetNextAlarmFromNow(due, TimeSpan.FromMinutes(5));

        _clock.SetUtcNow(EventStart.AddMinutes(-10));
        Assert.IsTrue(_service.CollectDueAlarms().Any(a => a.IsSnoozeReminder), "The 5-minute snooze rings");

        _clock.SetUtcNow(EventStart.AddMinutes(-5));
        Assert.AreEqual(5, _service.CollectDueAlarms().Single().OffsetMinutes, "The 5-minute offset fires");

        _clock.SetUtcNow(EventStart.AddMinutes(-1));
        Assert.AreEqual(1, _service.CollectDueAlarms().Single().OffsetMinutes, "The 1-minute offset fires");

        _clock.SetUtcNow(EventStart);
        Assert.AreEqual(0, _service.CollectDueAlarms().Single().OffsetMinutes, "The start time fires");
    }

    [TestMethod]
    public void SetNextAlert_DoesNotAffectOtherEvents()
    {
        SaveEvent("ev1");
        SaveEvent("ev2");
        _clock.SetUtcNow(EventStart.AddMinutes(-15));
        var due1 = _service.CollectDueAlarms().Single(a => a.EventId == "ev1");

        _service.SetNextAlarmFromNow(due1, TimeSpan.FromMinutes(20));

        _clock.SetUtcNow(EventStart.AddMinutes(-5));
        var due = _service.CollectDueAlarms();
        Assert.HasCount(1, due, "The 5-minute offset of ev2 fires as usual");
        Assert.AreEqual("ev2", due[0].EventId);
    }

    [TestMethod]
    public void GetNextAlert_ReturnsUnconsumedNotificationTimeOfSameOccurrence()
    {
        SaveEvent("ev");
        var occStart = EventStart.DateTime;

        Assert.AreEqual(
            occStart.AddMinutes(-15),
            _service.GetNextAlarmTimeForOccurrence("ev", occStart, _clock.GetLocalNow().DateTime),
            "The nearest unconsumed one is 15 minutes before");

        _clock.SetUtcNow(EventStart.AddMinutes(-15));
        _service.CollectDueAlarms();

        Assert.AreEqual(
            occStart.AddMinutes(-5),
            _service.GetNextAlarmTimeForOccurrence("ev", occStart, _clock.GetLocalNow().DateTime),
            "After the 15-minute one is consumed, the next is 5 minutes before");
    }

    [TestMethod]
    public void GetNextAlert_NoneRemaining_ReturnsNull()
    {
        SaveEvent("ev");
        var occStart = EventStart.DateTime;

        foreach (var minutes in new[] { -15, -5, -1, 0 })
        {
            _clock.SetUtcNow(EventStart.AddMinutes(minutes));
            _service.CollectDueAlarms();
        }

        Assert.IsNull(
            _service.GetNextAlarmTimeForOccurrence("ev", occStart, _clock.GetLocalNow().DateTime),
            "Null after all offsets are consumed");
    }

    [TestMethod]
    public void GetNextAlert_RecurringEvent_ConsidersOnlyTodaysOccurrence()
    {
        // Repeats daily (every weekday), so both today (6/10) and tomorrow (6/11) have a 10:00 occurrence.
        SaveDailyRecurringEvent("daily");
        var todayOccStart = EventStart.DateTime;

        // Consume all of today's offsets
        foreach (var minutes in new[] { -15, -5, -1, 0 })
        {
            _clock.SetUtcNow(EventStart.AddMinutes(minutes));
            _service.CollectDueAlarms();
        }

        // The schedule list contains the 15-minute alert of tomorrow's occurrence (6/11 10:00)
        Assert.IsTrue(
            _service.GetScheduledAlarms().Any(e => e.NotifyAt == EventStart.AddDays(1).AddMinutes(-15).DateTime),
            "Tomorrow's occurrence is in the schedule list");

        // But the "next alert", limited to today's occurrence, does not roll over to tomorrow
        Assert.IsNull(
            _service.GetNextAlarmTimeForOccurrence("daily", todayOccStart, _clock.GetLocalNow().DateTime),
            "Once today's occurrence is consumed, tomorrow's is not the next alert");
    }

    [TestMethod]
    public void HasRemainingAlarms_TrueOnlyWhenFutureUnfiredNotificationsExist()
    {
        SaveEvent("ev");
        _clock.SetUtcNow(EventStart.AddMinutes(-15));
        _service.CollectDueAlarms(); // Consume the 15-minute alert

        Assert.IsTrue(_service.HasRemainingAlarms("ev"), "The 5-min/1-min/start-time alerts are still in the future");

        // Consume all offsets
        _clock.SetUtcNow(EventStart.AddMinutes(-5));
        _service.CollectDueAlarms();
        _clock.SetUtcNow(EventStart.AddMinutes(-1));
        _service.CollectDueAlarms();
        _clock.SetUtcNow(EventStart);
        _service.CollectDueAlarms();

        Assert.IsFalse(_service.HasRemainingAlarms("ev"), "Nothing remains after all offsets are consumed");
    }

    [TestMethod]
    public void HasUnshownDueAlarm_TrueOnlyWhenNewAlertArrives()
    {
        SaveEvent("ev");
        _clock.SetUtcNow(EventStart.AddMinutes(-15));
        _service.CollectDueAlarms(); // Show the 15-minute alert (fired)

        Assert.IsFalse(_service.HasUnshownDueAlarm("ev"), "No unshown due alarm before the 5-minute time");

        _clock.SetUtcNow(EventStart.AddMinutes(-5));
        Assert.IsTrue(_service.HasUnshownDueAlarm("ev"), "The 5-minute alarm is due and unshown");
        Assert.IsFalse(_service.HasUnshownDueAlarm("other"), "Does not react to another event");
    }

    [TestMethod]
    public void FiredKeys_SavedAndRestored_DoNotRefire()
    {
        SaveEvent("ev");
        _clock.SetUtcNow(EventStart.AddMinutes(-15));
        Assert.HasCount(1, _service.CollectDueAlarms());

        var snapshot = _service.GetFiredKeys();
        _service.ResetAllFiredKeys();
        _service.RestoreFiredKeys(snapshot);

        Assert.HasCount(0, _service.CollectDueAlarms(), "The same alarm does not fire again after restore");
    }

    // ── cancelled ──────────────────────────────────────────────────────────

    [TestMethod]
    public void CancelAll_StopsRemainingNotificationsAndSnoozes()
    {
        SaveEvent("ev");
        _clock.SetUtcNow(EventStart.AddMinutes(-15));
        var due = _service.CollectDueAlarms().Single();
        _service.SnoozeFor(due, TimeSpan.FromMinutes(1));

        _service.CancelRemainingAlarms("ev");

        _clock.SetUtcNow(EventStart);
        Assert.HasCount(0, _service.CollectDueAlarms(), "None of 5-min, 1-min, start-time or snooze fires");
    }

    [TestMethod]
    public void CancelAll_DoesNotAffectOtherEvents()
    {
        SaveEvent("ev1");
        SaveEvent("ev2");

        _service.CancelRemainingAlarms("ev1");

        _clock.SetUtcNow(EventStart.AddMinutes(-15));
        var due = _service.CollectDueAlarms();
        Assert.HasCount(1, due);
        Assert.AreEqual("ev2", due[0].EventId);
    }

    // ── reset ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void EventChange_ResetsFiredStateAndRefiresWithinGrace()
    {
        SaveEvent("ev");
        _clock.SetUtcNow(EventStart.AddMinutes(-15));
        Assert.HasCount(1, _service.CollectDueAlarms());

        // Repository change (Save raises Changed) clears the fired keys.
        SaveEvent("other");

        Assert.HasCount(0, _service.GetFiredKeys());
        var due = _service.CollectDueAlarms();
        Assert.IsTrue(due.Any(a => a.EventId == "ev"), "Fires again because the notification time is within grace");
    }

    [TestMethod]
    public void ResetFiredKeys_ClearsOnlyTargetEventHistory()
    {
        SaveEvent("ev1");
        SaveEvent("ev2");
        _clock.SetUtcNow(EventStart.AddMinutes(-15));
        Assert.HasCount(2, _service.CollectDueAlarms());

        _service.ResetFiredKeys("ev1");

        var due = _service.CollectDueAlarms();
        Assert.HasCount(1, due);
        Assert.AreEqual("ev1", due[0].EventId);
    }

    // ── schedule listing ───────────────────────────────────────────────────

    [TestMethod]
    public void ScheduleList_OrderedByNotificationTimeAndReflectsFiredFlag()
    {
        SaveEvent("ev");
        _clock.SetUtcNow(EventStart.AddMinutes(-15));
        _service.CollectDueAlarms();

        var entries = _service.GetScheduledAlarms();

        Assert.HasCount(4, entries);
        CollectionAssert.AreEqual(
            new[] { 15, 5, 1, 0 },
            entries.Select(e => e.MinutesBefore).ToArray(),
            "Ascending notification time = descending offset");
        Assert.IsTrue(entries.Single(e => e.MinutesBefore == 15).AlreadyFired);
        Assert.IsFalse(entries.Single(e => e.MinutesBefore == 5).AlreadyFired);
    }

    [TestMethod]
    public void AllDayEvent_HasNoAlarmsPlanned()
    {
        var start = new DateTimeOffset(2026, 6, 10, 0, 0, 0, TimeSpan.Zero);
        var ev = CalendarEvent.CreateSingle(
            new EventId("allday"), new EventTitle("AllDay"), null,
            Visibility.Public, null, null, new TimeZoneId("UTC"),
            new SingleEventSchedule(
                Utc(2026, 6, 10, 0, 0), 24 * 60),
            start,
            alarm: EventAlarm.Default);
        _repo.Save(ev);

        _clock.SetUtcNow(start.AddHours(9));
        Assert.HasCount(0, _service.CollectDueAlarms());
        Assert.HasCount(0, _service.GetScheduledAlarms());
    }

    // ── per-occurrence alarm suppression ───────────────────────────────────

    [TestMethod]
    public void RecurringEvent_CanDisableAlarmForThisOccurrenceOnly()
    {
        SaveDailyRecurringEvent("daily");

        // Backward-compat: silence only today's (6/10) occurrence via a legacy per-occurrence override.
        // In the new model this would be a SplitThisOccurrence, but existing stored data may carry override records.
        var ev = _repo.FindById(new EventId("daily"))!;
        var todayKey = new OccurrenceLocalKey(
            new LocalDateValue(EventStart.Year, EventStart.Month, EventStart.Day),
            new LocalTimeValue(10, 0, 0));
        var ov = new ExceptionOverride(alarmEnabled: false);
        var exceptions = new List<EventException> { EventException.CreateOverride(todayKey, ov) };
        var reconstituted = CalendarEvent.Reconstitute(
            ev.Id, ev.Kind, ev.Title, ev.Location, ev.Visibility,
            ev.EventType, ev.Description, ev.TimeZoneId,
            ev.SingleSchedule, ev.RecurringSchedule, ev.CreatedAt, ev.UpdatedAt,
            exceptions, [], ev.Version, ev.Alarm, ev.ColorKey);
        _repo.Save(reconstituted);

        // Today's 15-min alarm would normally be due here, but the occurrence is silenced.
        _clock.SetUtcNow(EventStart.AddMinutes(-15));
        Assert.HasCount(0, _service.CollectDueAlarms(), "This occurrence does not fire because its alarm is disabled");

        // The next day's occurrence is unaffected and still planned.
        Assert.IsTrue(
            _service.GetScheduledAlarms().Any(e => e.NotifyAt == EventStart.AddDays(1).AddMinutes(-15).DateTime),
            "Other occurrences are unaffected");
        Assert.IsFalse(
            _service.GetScheduledAlarms().Any(e => e.NotifyAt == EventStart.AddMinutes(-15).DateTime),
            "The disabled occurrence is not in the schedule list either");
    }

    [TestMethod]
    public void DisablingAlarm_DoesNotExtendToOtherOccurrences()
    {
        SaveDailyRecurringEvent("daily");
        var ev = _repo.FindById(new EventId("daily"))!;
        // Silence tomorrow (6/11), not today.
        var tomorrowKey = new OccurrenceLocalKey(
            new LocalDateValue(EventStart.Year, EventStart.Month, EventStart.Day).AddDays(1),
            new LocalTimeValue(10, 0, 0));
        var ov = new ExceptionOverride(alarmEnabled: false);
        var exceptions = new List<EventException> { EventException.CreateOverride(tomorrowKey, ov) };
        var reconstituted = CalendarEvent.Reconstitute(
            ev.Id, ev.Kind, ev.Title, ev.Location, ev.Visibility,
            ev.EventType, ev.Description, ev.TimeZoneId,
            ev.SingleSchedule, ev.RecurringSchedule, ev.CreatedAt, ev.UpdatedAt,
            exceptions, [], ev.Version, ev.Alarm, ev.ColorKey);
        _repo.Save(reconstituted);

        _clock.SetUtcNow(EventStart.AddMinutes(-15));
        var due = _service.CollectDueAlarms();
        Assert.HasCount(1, due, "Today's occurrence fires as usual");
        Assert.AreEqual(15, due[0].OffsetMinutes);
    }

    // ── business-day shift ─────────────────────────────────────────────────

    [TestMethod]
    public void BusinessDayShiftedRecurrence_RingsOnShiftedDate()
    {
        // Monthly month-end minus 3 business days with a Mon–Fri calendar:
        // June 2026 ends Tue 6/30, so 3 business days before is Thu 6/25 at 10:00. The alarm must
        // follow the shifted date — earlier this used a null calendar and stayed on 6/30.
        _calendarRepo.Save(new BusinessCalendar(
            new BusinessCalendarId("jp"), "JP", new TimeZoneId("UTC"),
            [Weekday.Monday, Weekday.Tuesday, Weekday.Wednesday, Weekday.Thursday, Weekday.Friday],
            []));
        SaveMonthEndMinusBusinessDaysEvent("bd", "jp", 3);

        var shiftedStart = new DateTimeOffset(2026, 6, 25, 10, 0, 0, TimeSpan.Zero);

        // Nothing fires on the unshifted month-end (6/30): the occurrence moved to 6/25.
        _clock.SetUtcNow(new DateTimeOffset(2026, 6, 30, 9, 45, 0, TimeSpan.Zero));
        Assert.HasCount(0, _service.CollectDueAlarms(), "Does not ring on the unshifted month end (6/30)");

        // The 15-min alarm fires before the shifted occurrence on 6/25.
        _clock.SetUtcNow(shiftedStart.AddMinutes(-15));
        var due = _service.CollectDueAlarms();
        Assert.HasCount(1, due);
        Assert.AreEqual(shiftedStart.DateTime, due[0].OccurrenceStart);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the UTC instant for the given wall-clock components in the event's time zone.
    /// All events in these tests use the "UTC" zone, so the wall-clock equals the UTC instant.
    /// </summary>
    private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);

    private void SaveEvent(string id, EventAlarm? alarm = null)
    {
        var ev = CalendarEvent.CreateSingle(
            new EventId(id), new EventTitle($"Event {id}"), null,
            Visibility.Public, null, null, new TimeZoneId("UTC"),
            new SingleEventSchedule(EventStart, 60),
            EventStart.AddDays(-1),
            alarm: alarm ?? EventAlarm.Default);
        _repo.Save(ev);
    }

    /// <summary>A daily (every weekday) recurring event at the same time-of-day as <see cref="EventStart"/>.</summary>
    private void SaveDailyRecurringEvent(string id)
    {
        var startDate = new LocalDateValue(EventStart.Year, EventStart.Month, EventStart.Day);
        var rule = new RecurrenceRule(
            RecurrenceType.Weekly, 1, startDate.AddDays(30),
            // Monday-first: GenerateWeekly walks weekdays in week order and stops at the first one
            // past the range, so the list must be chronological within the (Monday-aligned) week.
            weekly: new WeeklyRule(
            [
                Weekday.Monday, Weekday.Tuesday, Weekday.Wednesday, Weekday.Thursday,
                Weekday.Friday, Weekday.Saturday, Weekday.Sunday
            ]));
        var schedule = new RecurringEventSchedule(
            EventStart,
            60,
            rule);
        var ev = CalendarEvent.CreateRecurring(
            new EventId(id), new EventTitle($"Event {id}"), null,
            Visibility.Public, null, null, new TimeZoneId("UTC"),
            schedule, EventStart.AddDays(-1),
            alarm: EventAlarm.Default);
        _repo.Save(ev);
    }

    /// <summary>
    /// A monthly "last day of month minus N business days at 10:00" recurring event whose
    /// business-day adjustment references the given calendar id.
    /// </summary>
    private void SaveMonthEndMinusBusinessDaysEvent(string id, string calendarId, int businessDaysBefore)
    {
        var rule = new RecurrenceRule(
            RecurrenceType.Monthly, 1, new LocalDateValue(9999, 12, 31),
            monthly: new LastDayOfMonthMonthlyRule(),
            adjustment: new AdjustmentRule(
                AdjustmentCondition.Always, AdjustmentShiftUnit.BusinessDay,
                -businessDaysBefore, new BusinessCalendarId(calendarId)));
        var schedule = new RecurringEventSchedule(Utc(2026, 6, 1, 10, 0), 60, rule);
        var ev = CalendarEvent.CreateRecurring(
            new EventId(id), new EventTitle($"Event {id}"), null,
            Visibility.Public, null, null, new TimeZoneId("UTC"),
            schedule, EventStart.AddDays(-1),
            alarm: EventAlarm.Default);
        _repo.Save(ev);
    }
}
