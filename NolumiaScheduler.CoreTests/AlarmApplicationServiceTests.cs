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
    public void 通知時刻前は何も発火しない()
    {
        SaveEvent("ev");
        _clock.SetUtcNow(EventStart.AddMinutes(-16));

        Assert.HasCount(0, _service.CollectDueAlarms());
        Assert.HasCount(0, _service.GetFiredKeys());
    }

    [TestMethod]
    public void 通知時刻を迎えると発火し発火済みに遷移する()
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
    public void 発火済みのアラームは同時刻に再収集しても発火しない()
    {
        SaveEvent("ev");
        _clock.SetUtcNow(EventStart.AddMinutes(-15));

        Assert.HasCount(1, _service.CollectDueAlarms());
        Assert.HasCount(0, _service.CollectDueAlarms(), "2回目は発火済みなので空");
    }

    [TestMethod]
    public void 時間経過で15分前_5分前_1分前_開始時刻と順に遷移する()
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
        Assert.HasCount(0, _service.CollectDueAlarms(), "全オフセット消化後は何もない");
    }

    [TestMethod]
    public void 猶予時間を過ぎた通知は発火せずスキップされる()
    {
        SaveEvent("ev");
        // 15-min notify time passed 2 minutes ago — beyond the catch-up grace (1 min).
        _clock.SetUtcNow(EventStart.AddMinutes(-13));

        Assert.HasCount(0, _service.CollectDueAlarms());
    }

    [TestMethod]
    public void 無効化されたアラームは発火しない()
    {
        SaveEvent("ev", new EventAlarm(IsEnabled: false, true, true, true, true));
        _clock.SetUtcNow(EventStart.AddMinutes(-15));

        Assert.HasCount(0, _service.CollectDueAlarms());
    }

    [TestMethod]
    public void オフセット個別設定_有効なものだけ発火する()
    {
        SaveEvent("ev", new EventAlarm(true, Notify15Min: false, Notify5Min: true, Notify1Min: false, NotifyAtStart: false));

        _clock.SetUtcNow(EventStart.AddMinutes(-15));
        Assert.HasCount(0, _service.CollectDueAlarms());

        _clock.SetUtcNow(EventStart.AddMinutes(-5));
        Assert.AreEqual(5, _service.CollectDueAlarms().Single().OffsetMinutes);
    }

    // ── fired → snoozed → re-fired ─────────────────────────────────────────

    [TestMethod]
    public void スヌーズすると指定時間後に再通知される()
    {
        SaveEvent("ev");
        _clock.SetUtcNow(EventStart.AddMinutes(-15));
        var due = _service.CollectDueAlarms().Single();

        _service.SnoozeFor(due, TimeSpan.FromMinutes(5));

        _clock.Advance(TimeSpan.FromMinutes(4));
        Assert.HasCount(0, _service.CollectDueAlarms(), "スヌーズ期限前は再通知しない");

        _clock.Advance(TimeSpan.FromMinutes(1));
        var reminder = _service.CollectDueAlarms();
        Assert.HasCount(1, reminder.Where(a => a.IsSnoozeReminder).ToList());
        Assert.AreEqual("ev", reminder.First(a => a.IsSnoozeReminder).EventId);
    }

    [TestMethod]
    public void 開始5分前スヌーズは開始5分前に再通知される()
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
    public void 開始時刻情報がないアラームの開始前スヌーズは無視される()
    {
        var orphan = new DueAlarm("ev", "title", null, 0, true, OccurrenceStart: null);

        _service.SnoozeUntilBeforeStart(orphan, TimeSpan.FromMinutes(5));

        Assert.HasCount(0, _service.GetScheduledAlarms().Where(e => e.IsSnoozed).ToList());
    }

    // ── next-alarm override (explicit time wins over offsets) ───────────────

    [TestMethod]
    public void 次のアラート設定_今からの指定で全オフセットを抑止し指定時刻に一度だけ再通知する()
    {
        SaveEvent("ev");
        _clock.SetUtcNow(EventStart.AddMinutes(-15));
        var due = _service.CollectDueAlarms().Single();

        // 再通知時刻 = (EventStart-15) + 20 = EventStart+5
        _service.SetNextAlarmFromNow(due, TimeSpan.FromMinutes(20));

        _clock.SetUtcNow(EventStart.AddMinutes(-5));
        Assert.HasCount(0, _service.CollectDueAlarms(), "5分前オフセットは抑止される");

        _clock.SetUtcNow(EventStart);
        Assert.HasCount(0, _service.CollectDueAlarms(), "開始時刻オフセットも抑止される");

        _clock.SetUtcNow(EventStart.AddMinutes(5));
        var reminder = _service.CollectDueAlarms();
        Assert.HasCount(1, reminder);
        Assert.IsTrue(reminder[0].IsSnoozeReminder);
        Assert.AreEqual("ev", reminder[0].EventId);
    }

    [TestMethod]
    public void 次のアラート設定_指定時刻より前のオフセットだけ抑止し後のオフセットは残る()
    {
        SaveEvent("ev");
        _clock.SetUtcNow(EventStart.AddMinutes(-15));
        var due = _service.CollectDueAlarms().Single();

        // 次のアラート = 開始3分前。これより前の5分前は抑止、後の1分前・開始時刻は残る。
        _service.SetNextAlarmBeforeStart(due, TimeSpan.FromMinutes(3));

        _clock.SetUtcNow(EventStart.AddMinutes(-5));
        Assert.HasCount(0, _service.CollectDueAlarms(), "指定時刻より前の5分前は抑止される");

        _clock.SetUtcNow(EventStart.AddMinutes(-3));
        Assert.IsTrue(_service.CollectDueAlarms().Any(a => a.IsSnoozeReminder), "開始3分前に明示アラートが鳴る");

        _clock.SetUtcNow(EventStart.AddMinutes(-1));
        Assert.AreEqual(1, _service.CollectDueAlarms().Single().OffsetMinutes, "指定時刻より後の1分前は残る");

        _clock.SetUtcNow(EventStart);
        Assert.AreEqual(0, _service.CollectDueAlarms().Single().OffsetMinutes, "開始時刻も残る");
    }

    [TestMethod]
    public void スヌーズが残りオフセットより前なら5分前1分前開始時刻は発火し続ける()
    {
        SaveEvent("ev");
        _clock.SetUtcNow(EventStart.AddMinutes(-15));
        var due = _service.CollectDueAlarms().Single(); // 15分前アラート

        // 今から5分後 = 開始10分前。残りの5分前/1分前/開始時刻はすべて後なので発火済みにしない。
        _service.SetNextAlarmFromNow(due, TimeSpan.FromMinutes(5));

        _clock.SetUtcNow(EventStart.AddMinutes(-10));
        Assert.IsTrue(_service.CollectDueAlarms().Any(a => a.IsSnoozeReminder), "5分後のスヌーズが鳴る");

        _clock.SetUtcNow(EventStart.AddMinutes(-5));
        Assert.AreEqual(5, _service.CollectDueAlarms().Single().OffsetMinutes, "5分前は発火する");

        _clock.SetUtcNow(EventStart.AddMinutes(-1));
        Assert.AreEqual(1, _service.CollectDueAlarms().Single().OffsetMinutes, "1分前は発火する");

        _clock.SetUtcNow(EventStart);
        Assert.AreEqual(0, _service.CollectDueAlarms().Single().OffsetMinutes, "開始時刻は発火する");
    }

    [TestMethod]
    public void 次のアラート設定は他のイベントへ影響しない()
    {
        SaveEvent("ev1");
        SaveEvent("ev2");
        _clock.SetUtcNow(EventStart.AddMinutes(-15));
        var due1 = _service.CollectDueAlarms().Single(a => a.EventId == "ev1");

        _service.SetNextAlarmFromNow(due1, TimeSpan.FromMinutes(20));

        _clock.SetUtcNow(EventStart.AddMinutes(-5));
        var due = _service.CollectDueAlarms();
        Assert.HasCount(1, due, "ev2 の5分前は通常どおり発火する");
        Assert.AreEqual("ev2", due[0].EventId);
    }

    [TestMethod]
    public void 次のアラート取得は同じ回の未消化通知時刻を返す()
    {
        SaveEvent("ev");
        var occStart = EventStart.DateTime;

        Assert.AreEqual(
            occStart.AddMinutes(-15),
            _service.GetNextAlarmTimeForOccurrence("ev", occStart, _clock.GetLocalNow().DateTime),
            "最も近い未消化は15分前");

        _clock.SetUtcNow(EventStart.AddMinutes(-15));
        _service.CollectDueAlarms();

        Assert.AreEqual(
            occStart.AddMinutes(-5),
            _service.GetNextAlarmTimeForOccurrence("ev", occStart, _clock.GetLocalNow().DateTime),
            "15分前を消化したら次は5分前");
    }

    [TestMethod]
    public void 次のアラート取得は残りがなければnull()
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
            "全オフセット消化後は null");
    }

    [TestMethod]
    public void 次のアラート取得は繰り返し予定でも当日の回だけを対象にする()
    {
        // 毎日(全曜日)繰り返しなので、今日(6/10)と明日(6/11)の両方に10:00の回がある。
        SaveDailyRecurringEvent("daily");
        var todayOccStart = EventStart.DateTime;

        // 今日の全オフセットを消化する
        foreach (var minutes in new[] { -15, -5, -1, 0 })
        {
            _clock.SetUtcNow(EventStart.AddMinutes(minutes));
            _service.CollectDueAlarms();
        }

        // 翌日の回(6/11 10:00)の15分前が予定一覧には存在する
        Assert.IsTrue(
            _service.GetScheduledAlarms().Any(e => e.NotifyAt == EventStart.AddDays(1).AddMinutes(-15).DateTime),
            "翌日の回は予定一覧には含まれる");

        // しかし当日の回に絞った「次のアラート」は翌日へロールオーバーしない
        Assert.IsNull(
            _service.GetNextAlarmTimeForOccurrence("daily", todayOccStart, _clock.GetLocalNow().DateTime),
            "当日の回を消化したら翌日の回は次のアラートにしない");
    }

    [TestMethod]
    public void 残りアラート判定は将来の未発火通知があるときだけtrue()
    {
        SaveEvent("ev");
        _clock.SetUtcNow(EventStart.AddMinutes(-15));
        _service.CollectDueAlarms(); // 15分前を消化

        Assert.IsTrue(_service.HasRemainingAlarms("ev"), "5分前/1分前/開始時刻がまだ将来に残る");

        // 全オフセットを消化する
        _clock.SetUtcNow(EventStart.AddMinutes(-5));
        _service.CollectDueAlarms();
        _clock.SetUtcNow(EventStart.AddMinutes(-1));
        _service.CollectDueAlarms();
        _clock.SetUtcNow(EventStart);
        _service.CollectDueAlarms();

        Assert.IsFalse(_service.HasRemainingAlarms("ev"), "全オフセット消化後は残りなし");
    }

    [TestMethod]
    public void 未表示の発火中アラート判定は新しいアラートが来たときだけtrue()
    {
        SaveEvent("ev");
        _clock.SetUtcNow(EventStart.AddMinutes(-15));
        _service.CollectDueAlarms(); // 15分前を表示(発火済み)

        Assert.IsFalse(_service.HasUnshownDueAlarm("ev"), "5分前の時刻前は未表示の発火中アラートなし");

        _clock.SetUtcNow(EventStart.AddMinutes(-5));
        Assert.IsTrue(_service.HasUnshownDueAlarm("ev"), "5分前が発火中かつ未表示");
        Assert.IsFalse(_service.HasUnshownDueAlarm("other"), "別イベントには反応しない");
    }

    [TestMethod]
    public void 発火済みキーを退避して復元すると再発火しない()
    {
        SaveEvent("ev");
        _clock.SetUtcNow(EventStart.AddMinutes(-15));
        Assert.HasCount(1, _service.CollectDueAlarms());

        var snapshot = _service.GetFiredKeys();
        _service.ResetAllFiredKeys();
        _service.RestoreFiredKeys(snapshot);

        Assert.HasCount(0, _service.CollectDueAlarms(), "復元後は同じアラームが再発火しない");
    }

    // ── cancelled ──────────────────────────────────────────────────────────

    [TestMethod]
    public void 全キャンセルで残りの通知もスヌーズも発火しなくなる()
    {
        SaveEvent("ev");
        _clock.SetUtcNow(EventStart.AddMinutes(-15));
        var due = _service.CollectDueAlarms().Single();
        _service.SnoozeFor(due, TimeSpan.FromMinutes(1));

        _service.CancelRemainingAlarms("ev");

        _clock.SetUtcNow(EventStart);
        Assert.HasCount(0, _service.CollectDueAlarms(), "5分前・1分前・開始時刻・スヌーズすべて発火しない");
    }

    [TestMethod]
    public void 全キャンセルは他のイベントへ影響しない()
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
    public void イベント変更で発火済み状態がリセットされ猶予内なら再発火する()
    {
        SaveEvent("ev");
        _clock.SetUtcNow(EventStart.AddMinutes(-15));
        Assert.HasCount(1, _service.CollectDueAlarms());

        // Repository change (Save raises Changed) clears the fired keys.
        SaveEvent("other");

        Assert.HasCount(0, _service.GetFiredKeys());
        var due = _service.CollectDueAlarms();
        Assert.IsTrue(due.Any(a => a.EventId == "ev"), "通知時刻が猶予内なので再発火する");
    }

    [TestMethod]
    public void ResetFiredKeysは対象イベントの発火履歴だけ消す()
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
    public void 予定一覧は通知時刻順で発火済みフラグを反映する()
    {
        SaveEvent("ev");
        _clock.SetUtcNow(EventStart.AddMinutes(-15));
        _service.CollectDueAlarms();

        var entries = _service.GetScheduledAlarms();

        Assert.HasCount(4, entries);
        CollectionAssert.AreEqual(
            new[] { 15, 5, 1, 0 },
            entries.Select(e => e.MinutesBefore).ToArray(),
            "通知時刻の昇順 = オフセットの降順");
        Assert.IsTrue(entries.Single(e => e.MinutesBefore == 15).AlreadyFired);
        Assert.IsFalse(entries.Single(e => e.MinutesBefore == 5).AlreadyFired);
    }

    [TestMethod]
    public void 終日イベントにはアラームが計画されない()
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
    public void 繰り返しのこの回だけアラームを無効化できる()
    {
        SaveDailyRecurringEvent("daily");

        // Silence only today's (6/10) occurrence via a per-occurrence override.
        var ev = _repo.FindById(new EventId("daily"))!;
        var todayKey = new OccurrenceLocalKey(
            new LocalDateValue(EventStart.Year, EventStart.Month, EventStart.Day),
            new LocalTimeValue(10, 0, 0));
        ev.OverrideOccurrence(todayKey, new ExceptionOverride(alarmEnabled: false), EventStart.AddDays(-1));
        _repo.Save(ev);

        // Today's 15-min alarm would normally be due here, but the occurrence is silenced.
        _clock.SetUtcNow(EventStart.AddMinutes(-15));
        Assert.HasCount(0, _service.CollectDueAlarms(), "この回はアラーム無効なので発火しない");

        // The next day's occurrence is unaffected and still planned.
        Assert.IsTrue(
            _service.GetScheduledAlarms().Any(e => e.NotifyAt == EventStart.AddDays(1).AddMinutes(-15).DateTime),
            "他の回は影響を受けない");
        Assert.IsFalse(
            _service.GetScheduledAlarms().Any(e => e.NotifyAt == EventStart.AddMinutes(-15).DateTime),
            "無効化した回は予定一覧にも出ない");
    }

    [TestMethod]
    public void アラーム無効化は他の回には及ばない()
    {
        SaveDailyRecurringEvent("daily");
        var ev = _repo.FindById(new EventId("daily"))!;
        // Silence tomorrow (6/11), not today.
        var tomorrowKey = new OccurrenceLocalKey(
            new LocalDateValue(EventStart.Year, EventStart.Month, EventStart.Day).AddDays(1),
            new LocalTimeValue(10, 0, 0));
        ev.OverrideOccurrence(tomorrowKey, new ExceptionOverride(alarmEnabled: false), EventStart.AddDays(-1));
        _repo.Save(ev);

        _clock.SetUtcNow(EventStart.AddMinutes(-15));
        var due = _service.CollectDueAlarms();
        Assert.HasCount(1, due, "今日の回は通常どおり発火する");
        Assert.AreEqual(15, due[0].OffsetMinutes);
    }

    // ── business-day shift ─────────────────────────────────────────────────

    [TestMethod]
    public void 営業日シフトの繰り返しはシフト後の日付でアラームが鳴る()
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
        Assert.HasCount(0, _service.CollectDueAlarms(), "シフト前の月末(6/30)では鳴らない");

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
