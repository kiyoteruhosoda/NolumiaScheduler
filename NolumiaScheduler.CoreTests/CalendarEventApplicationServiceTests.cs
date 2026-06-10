using NolumiaScheduler.Application.Commands;
using NolumiaScheduler.Application.Services;
using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.Entities;
using NolumiaScheduler.Domain.Repositories;
using NolumiaScheduler.Domain.ValueObjects;
using Visibility = NolumiaScheduler.Domain.ValueObjects.Visibility;

namespace NolumiaScheduler.CoreTests;

[TestClass]
public class CalendarEventApplicationServiceTests
{
    private InMemoryCalendarEventRepository _repo = null!;
    private FakeClock _clock = null!;
    private CalendarEventApplicationService _svc = null!;

    [TestInitialize]
    public void Setup()
    {
        _repo = new InMemoryCalendarEventRepository();
        _clock = new FakeClock(new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero));
        _svc = new CalendarEventApplicationService(_repo, _repo, _clock);
    }

    // ── CreateSingleEvent ──────────────────────────────────────────────────

    [TestMethod]
    public void CreateSingleEvent_単発予定が保存される()
    {
        _svc.CreateSingleEvent(new CreateSingleEventCommand(
            Title: "Meeting",
            Location: "Room A",
            Visibility: Visibility.Public,
            EventType: null,
            Description: null,
            TimeZone: "Asia/Tokyo",
            AllDay: false,
            StartDate: new DateOnly(2026, 5, 20),
            StartTime: new TimeSpan(10, 0, 0),
            EndTime: new TimeSpan(11, 0, 0)));

        var all = _repo.FindAll();
        Assert.HasCount(1, all);
        Assert.AreEqual("Meeting", all[0].Title.Value);
        Assert.AreEqual("Room A", all[0].Location!.Value);
        Assert.IsTrue(all[0].IsSingle());
    }

    [TestMethod]
    public void CreateSingleEvent_終日予定はスケジュールが1日分になる()
    {
        _svc.CreateSingleEvent(new CreateSingleEventCommand(
            Title: "Holiday",
            Location: null,
            Visibility: Visibility.Public,
            EventType: null,
            Description: null,
            TimeZone: "Asia/Tokyo",
            AllDay: true,
            StartDate: new DateOnly(2026, 5, 20),
            StartTime: TimeSpan.Zero,
            EndTime: TimeSpan.Zero));

        var ev = _repo.FindAll()[0];
        Assert.IsTrue(ev.AllDay);
        var duration = ev.SingleSchedule!.End - ev.SingleSchedule.Start;
        Assert.AreEqual(TimeSpan.FromDays(1), duration);
    }

    [TestMethod]
    public void CreateSingleEvent_作成日時と更新日時はTimeProviderの現在時刻になる()
    {
        _svc.CreateSingleEvent(new CreateSingleEventCommand(
            Title: "Clock",
            Location: null,
            Visibility: Visibility.Public,
            EventType: null,
            Description: null,
            TimeZone: "Asia/Tokyo",
            AllDay: false,
            StartDate: new DateOnly(2026, 5, 20),
            StartTime: new TimeSpan(10, 0, 0),
            EndTime: new TimeSpan(11, 0, 0)));

        var ev = _repo.FindAll()[0];
        Assert.AreEqual(_clock.GetUtcNow(), ev.CreatedAt);
        Assert.AreEqual(_clock.GetUtcNow(), ev.UpdatedAt);
    }

    [TestMethod]
    public void UpdateEvent_更新日時は進んだクロックの時刻に遷移する()
    {
        CreateAndSaveSingleEvent("clk1");
        _clock.Advance(TimeSpan.FromHours(3));

        _svc.UpdateEvent(new UpdateEventCommand(
            EventId: "clk1",
            Title: "Touched",
            Location: null,
            Visibility: Visibility.Public,
            AllDay: false,
            NewDate: null,
            NewStartTime: null,
            NewEndTime: null,
            Alarm: null));

        var saved = _repo.FindById(new EventId("clk1"))!;
        Assert.AreEqual(_clock.GetUtcNow(), saved.UpdatedAt);
    }

    // ── CreateRecurringEvent ───────────────────────────────────────────────

    [TestMethod]
    public void CreateRecurringEvent_繰り返し予定が保存される()
    {
        var rule = new RecurrenceRule(
            RecurrenceType.Weekly, 1,
            new LocalDateValue(2026, 12, 31),
            weekly: new WeeklyRule([Weekday.Wednesday]));

        _svc.CreateRecurringEvent(new CreateRecurringEventCommand(
            Title: "Standup",
            Location: null,
            Visibility: Visibility.Public,
            EventType: null,
            Description: null,
            TimeZone: "Asia/Tokyo",
            AllDay: false,
            StartDate: new LocalDateValue(2026, 5, 6),
            StartTime: new LocalTimeValue(9, 30, 0),
            EndTime: new LocalTimeValue(10, 0, 0),
            RecurrenceRule: rule));

        var all = _repo.FindAll();
        Assert.HasCount(1, all);
        Assert.IsTrue(all[0].IsRecurring());
        Assert.AreEqual("Standup", all[0].Title.Value);
    }

    // ── UpdateEvent ────────────────────────────────────────────────────────

    [TestMethod]
    public void UpdateEvent_タイトルと場所を変更できる()
    {
        var ev = CreateAndSaveSingleEvent("upd1");
        _svc.UpdateEvent(new UpdateEventCommand(
            EventId: "upd1",
            Title: "Updated",
            Location: "Room B",
            Visibility: Visibility.Public,
            AllDay: false,
            NewDate: null,
            NewStartTime: null,
            NewEndTime: null,
            Alarm: null));

        var saved = _repo.FindById(new EventId("upd1"))!;
        Assert.AreEqual("Updated", saved.Title.Value);
        Assert.AreEqual("Room B", saved.Location!.Value);
    }

    [TestMethod]
    public void UpdateEvent_単発予定は日時を変更できる()
    {
        var ev = CreateAndSaveSingleEvent("upd2");
        var originalStart = ev.SingleSchedule!.Start;

        _svc.UpdateEvent(new UpdateEventCommand(
            EventId: "upd2",
            Title: "x",
            Location: null,
            Visibility: Visibility.Public,
            AllDay: false,
            NewDate: new DateOnly(2026, 6, 1),
            NewStartTime: new TimeSpan(14, 0, 0),
            NewEndTime: new TimeSpan(15, 0, 0),
            Alarm: null));

        var saved = _repo.FindById(new EventId("upd2"))!;
        Assert.AreNotEqual(originalStart, saved.SingleSchedule!.Start);
        Assert.AreEqual(14, saved.SingleSchedule.Start.ToOffset(TimeSpan.FromHours(9)).Hour);
    }

    [TestMethod]
    public void UpdateEvent_終日単発予定は時間なしで日付変更できる()
    {
        _svc.CreateSingleEvent(new CreateSingleEventCommand(
            Title: "All Day", Location: null, Visibility: Visibility.Public,
            EventType: null, Description: null, TimeZone: "Asia/Tokyo",
            AllDay: true,
            StartDate: new DateOnly(2026, 5, 20),
            StartTime: TimeSpan.Zero, EndTime: TimeSpan.Zero));

        var ev = _repo.FindAll()[0];
        var originalStart = ev.SingleSchedule!.Start;

        _svc.UpdateEvent(new UpdateEventCommand(
            EventId: ev.Id.Value,
            Title: "All Day", Location: null, Visibility: Visibility.Public,
            AllDay: true,
            NewDate: new DateOnly(2026, 5, 25),
            NewStartTime: null,
            NewEndTime: null,
            Alarm: null));

        var saved = _repo.FindById(ev.Id)!;
        Assert.AreNotEqual(originalStart, saved.SingleSchedule!.Start);
        Assert.AreEqual(25, saved.SingleSchedule.Start.ToOffset(TimeSpan.FromHours(9)).Day);
    }

    // ── SkipOccurrence / DeleteOccurrence ──────────────────────────────────

    [TestMethod]
    public void SkipOccurrence_対象Occurrenceがスキップされる()
    {
        SaveRecurringEvent("skip1");
        var key = new OccurrenceLocalKey(new LocalDateValue(2026, 5, 6), new LocalTimeValue(9, 30, 0));

        _svc.SkipOccurrence(new SkipOccurrenceCommand("skip1", key));

        var saved = _repo.FindById(new EventId("skip1"))!;
        Assert.HasCount(1, saved.Exceptions);
        Assert.AreEqual(ExceptionType.Skip, saved.Exceptions[0].Type);
        Assert.AreEqual(key, saved.Exceptions[0].OccurrenceKey);
    }

    [TestMethod]
    public void DeleteOccurrence_SkipOccurrenceと同等の動作をする()
    {
        SaveRecurringEvent("del1");
        var key = new OccurrenceLocalKey(new LocalDateValue(2026, 5, 6), new LocalTimeValue(9, 30, 0));

        _svc.DeleteOccurrence(new SkipOccurrenceCommand("del1", key));

        var saved = _repo.FindById(new EventId("del1"))!;
        Assert.HasCount(1, saved.Exceptions);
        Assert.AreEqual(ExceptionType.Skip, saved.Exceptions[0].Type);
    }

    // ── OverrideOccurrence ─────────────────────────────────────────────────

    [TestMethod]
    public void OverrideOccurrence_対象Occurrenceを上書きできる()
    {
        SaveRecurringEvent("ovr1");
        var key = new OccurrenceLocalKey(new LocalDateValue(2026, 5, 6), new LocalTimeValue(9, 30, 0));

        _svc.OverrideOccurrence(new OverrideOccurrenceCommand(
            EventId: "ovr1",
            OccurrenceKey: key,
            Title: "Special",
            Location: "Lab",
            Visibility: Visibility.Public,
            AllDay: false,
            Date: new LocalDateValue(2026, 5, 6),
            StartTime: new LocalTimeValue(10, 0, 0),
            EndTime: new LocalTimeValue(11, 0, 0)));

        var saved = _repo.FindById(new EventId("ovr1"))!;
        Assert.HasCount(1, saved.Exceptions);
        Assert.AreEqual(ExceptionType.Override, saved.Exceptions[0].Type);
    }

    [TestMethod]
    public void OverrideOccurrence_日付変更時はMoveも追加される()
    {
        SaveRecurringEvent("ovr2");
        var key = new OccurrenceLocalKey(new LocalDateValue(2026, 5, 6), new LocalTimeValue(9, 30, 0));

        _svc.OverrideOccurrence(new OverrideOccurrenceCommand(
            EventId: "ovr2",
            OccurrenceKey: key,
            Title: "Moved",
            Location: null,
            Visibility: Visibility.Public,
            AllDay: false,
            Date: new LocalDateValue(2026, 5, 7),
            StartTime: new LocalTimeValue(9, 30, 0),
            EndTime: new LocalTimeValue(10, 30, 0)));

        var saved = _repo.FindById(new EventId("ovr2"))!;
        Assert.IsGreaterThanOrEqualTo(saved.Moves.Count, 1);
    }

    // ── MoveOccurrence ─────────────────────────────────────────────────────

    [TestMethod]
    public void MoveOccurrence_対象Occurrenceを別日に移動できる()
    {
        SaveRecurringEvent("mv1");
        var key = new OccurrenceLocalKey(new LocalDateValue(2026, 5, 6), new LocalTimeValue(9, 30, 0));

        _svc.MoveOccurrence(new MoveOccurrenceCommand(
            EventId: "mv1",
            OccurrenceKey: key,
            NewDate: new LocalDateValue(2026, 5, 8),
            NewStartTime: new LocalTimeValue(11, 0, 0),
            NewEndTime: new LocalTimeValue(12, 0, 0),
            Title: null,
            Location: null,
            Visibility: null));

        var saved = _repo.FindById(new EventId("mv1"))!;
        Assert.HasCount(1, saved.Moves);
        Assert.AreEqual(new LocalDateValue(2026, 5, 8), saved.Moves[0].NewDate);
    }

    // ── ChangeFollowingOccurrences ─────────────────────────────────────────

    [TestMethod]
    public void ChangeFollowingOccurrences_元系列の終了日が分割点の前日になる()
    {
        SaveRecurringEvent("split1");
        var fromKey = new OccurrenceLocalKey(new LocalDateValue(2026, 5, 6), new LocalTimeValue(9, 30, 0));
        var rule = new RecurrenceRule(
            RecurrenceType.Weekly, 1,
            new LocalDateValue(2026, 12, 31),
            weekly: new WeeklyRule([Weekday.Wednesday]));

        _svc.ChangeFollowingOccurrences(new ChangeFollowingOccurrencesCommand(
            EventId: "split1",
            FromOccurrenceKey: fromKey,
            NewTitle: "New Series",
            NewLocation: null,
            NewVisibility: Visibility.Public,
            NewAllDay: false,
            NewStartTime: new LocalTimeValue(10, 0, 0),
            NewEndTime: new LocalTimeValue(11, 0, 0),
            NewRecurrenceRule: rule));

        var all = _repo.FindAll();
        Assert.HasCount(2, all);

        var original = all.Single(e => e.Id.Value == "split1");
        Assert.AreEqual(new LocalDateValue(2026, 5, 5), original.RecurringSchedule!.RecurrenceRule.EndDate);

        var newSeries = all.Single(e => e.Id.Value != "split1");
        Assert.AreEqual("New Series", newSeries.Title.Value);
        Assert.AreEqual(new LocalDateValue(2026, 5, 6), newSeries.RecurringSchedule!.StartDate);
    }

    // ── UpdateRecurringSeries ──────────────────────────────────────────────

    [TestMethod]
    public void UpdateRecurringSeries_繰り返しルールと時刻と詳細が更新される()
    {
        SaveRecurringEvent("series1");

        var newRule = new RecurrenceRule(
            RecurrenceType.Weekly, 2,
            new LocalDateValue(2026, 12, 31),
            weekly: new WeeklyRule([Weekday.Monday, Weekday.Friday]));

        _svc.UpdateRecurringSeries(new UpdateRecurringSeriesCommand(
            EventId: "series1",
            Title: "Renamed",
            Location: "Room B",
            Visibility: Visibility.Public,
            StartTime: new LocalTimeValue(13, 0, 0),
            EndTime: new LocalTimeValue(14, 0, 0),
            RecurrenceRule: newRule));

        var all = _repo.FindAll();
        Assert.HasCount(1, all);

        var ev = all[0];
        Assert.AreEqual("Renamed", ev.Title.Value);
        Assert.AreEqual("Room B", ev.Location!.Value);
        CollectionAssert.AreEquivalent(
            new[] { Weekday.Monday, Weekday.Friday },
            ev.RecurringSchedule!.RecurrenceRule.Weekly!.Weekdays.ToList());
        Assert.AreEqual(2, ev.RecurringSchedule.RecurrenceRule.Interval);
        Assert.AreEqual(13, ev.RecurringSchedule.StartTime!.Hour);
        // The original series start date is preserved so existing occurrence keys stay aligned.
        Assert.AreEqual(new LocalDateValue(2026, 5, 1), ev.RecurringSchedule.StartDate);
    }

    // ── DeleteEvent ────────────────────────────────────────────────────────

    [TestMethod]
    public void DeleteEvent_予定が削除される()
    {
        CreateAndSaveSingleEvent("del-ev");
        Assert.HasCount(1, _repo.FindAll());

        _svc.DeleteEvent("del-ev");

        Assert.HasCount(0, _repo.FindAll());
    }

    // ── helpers ───────────────────────────────────────────────────────────

    private CalendarEvent CreateAndSaveSingleEvent(string id)
    {
        var tz = new TimeZoneId("Asia/Tokyo");
        var start = new DateTimeOffset(2026, 5, 20, 9, 0, 0, TimeSpan.FromHours(9));
        var ev = CalendarEvent.CreateSingle(
            new EventId(id), new EventTitle("sample"), null,
            Visibility.Public, null, null, tz, false,
            new SingleEventSchedule(start, start.AddHours(1)),
            DateTimeOffset.UtcNow);
        _repo.Save(ev);
        return ev;
    }

    private void SaveRecurringEvent(string id)
    {
        var tz = new TimeZoneId("Asia/Tokyo");
        var rule = new RecurrenceRule(
            RecurrenceType.Weekly, 1,
            new LocalDateValue(2026, 12, 31),
            weekly: new WeeklyRule([Weekday.Wednesday]));
        var schedule = new RecurringEventSchedule(
            new LocalDateValue(2026, 5, 1),
            new LocalTimeValue(9, 30, 0),
            new LocalTimeValue(10, 30, 0),
            rule, false);
        var ev = CalendarEvent.CreateRecurring(
            new EventId(id), new EventTitle("rec"), null,
            Visibility.Public, null, null, tz, false,
            schedule, DateTimeOffset.UtcNow);
        _repo.Save(ev);
    }

    private sealed class InMemoryCalendarEventRepository : ICalendarEventRepository, ICalendarEventChanges
    {
        public event Action? Changed;
        private readonly Dictionary<string, CalendarEvent> _store = [];
        public CalendarEvent? FindById(EventId id) => _store.TryGetValue(id.Value, out var ev) ? ev : null;
        public IReadOnlyList<CalendarEvent> FindAll() => [.. _store.Values];
        public void Save(CalendarEvent ev) { _store[ev.Id.Value] = ev; Changed?.Invoke(); }
        public void Delete(EventId id) { _store.Remove(id.Value); Changed?.Invoke(); }
    }
}
