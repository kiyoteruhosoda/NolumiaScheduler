using NolumiaScheduler.Application.Services;
using NolumiaScheduler.Domain.Aggregates;
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
    private FakeClock _clock = null!;
    private AlarmApplicationService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _repo = new InMemoryCalendarEventRepository();
        _clock = new FakeClock(EventStart.AddHours(-1));
        _service = new AlarmApplicationService(
            _repo, _repo, new OccurrenceExpander(new BusinessDayShiftService()), _clock);
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
            Visibility.Public, null, null, new TimeZoneId("UTC"), allDay: true,
            new SingleEventSchedule(start, start.AddDays(1)), start,
            alarm: EventAlarm.Default);
        _repo.Save(ev);

        _clock.SetUtcNow(start.AddHours(9));
        Assert.HasCount(0, _service.CollectDueAlarms());
        Assert.HasCount(0, _service.GetScheduledAlarms());
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private void SaveEvent(string id, EventAlarm? alarm = null)
    {
        var ev = CalendarEvent.CreateSingle(
            new EventId(id), new EventTitle($"Event {id}"), null,
            Visibility.Public, null, null, new TimeZoneId("UTC"), allDay: false,
            new SingleEventSchedule(EventStart, EventStart.AddHours(1)),
            EventStart.AddDays(-1),
            alarm: alarm ?? EventAlarm.Default);
        _repo.Save(ev);
    }
}
