using NolumiaScheduler.Application.Services;
using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.Repositories;
using NolumiaScheduler.Domain.Services;
using NolumiaScheduler.Domain.ValueObjects;
using Visibility = NolumiaScheduler.Domain.ValueObjects.Visibility;

namespace NolumiaScheduler.CoreTests;

/// <summary>
/// Date/time-driven purge transitions: an event survives while inside the retention window
/// and is deleted once the clock moves past it.
/// </summary>
[TestClass]
public class PurgeExpiredEventsServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);

    private InMemoryCalendarEventRepository _repo = null!;
    private InMemoryBusinessCalendarRepository _calendarRepo = null!;
    private FakeClock _clock = null!;
    private PurgeExpiredEventsService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _repo = new InMemoryCalendarEventRepository();
        _calendarRepo = new InMemoryBusinessCalendarRepository();
        _clock = new FakeClock(Now);
        _service = new PurgeExpiredEventsService(
            _repo, _calendarRepo,
            new EventExpirationService(new OccurrenceExpander(new BusinessDayShiftService())),
            _clock);
    }

    [TestMethod]
    public void 保持期間より前に終了したイベントは削除される()
    {
        _repo.Save(SingleEvent("old", end: Now.AddDays(-2)));
        _repo.Save(SingleEvent("future", end: Now.AddDays(2)));

        var purged = _service.PurgeExpiredEvents();

        Assert.AreEqual(1, purged);
        Assert.IsNull(_repo.FindById(new EventId("old")));
        Assert.IsNotNull(_repo.FindById(new EventId("future")));
    }

    [TestMethod]
    public void 終了直後のイベントは保持期間内なので残る()
    {
        _repo.Save(SingleEvent("just-ended", end: Now.AddHours(-2)));

        Assert.AreEqual(0, _service.PurgeExpiredEvents());
        Assert.IsNotNull(_repo.FindById(new EventId("just-ended")));
    }

    [TestMethod]
    public void 保持期間の境界_丁度1日前に終了したものは削除される()
    {
        _repo.Save(SingleEvent("boundary", end: Now - PurgeExpiredEventsService.DefaultRetention));
        _repo.Save(SingleEvent("inside", end: Now - PurgeExpiredEventsService.DefaultRetention + TimeSpan.FromSeconds(1)));

        Assert.AreEqual(1, _service.PurgeExpiredEvents());
        Assert.IsNull(_repo.FindById(new EventId("boundary")));
        Assert.IsNotNull(_repo.FindById(new EventId("inside")));
    }

    [TestMethod]
    public void 時間経過で残存イベントが削除対象に遷移する()
    {
        _repo.Save(SingleEvent("ev", end: Now.AddHours(1)));

        Assert.AreEqual(0, _service.PurgeExpiredEvents(), "終了前: 削除されない");

        _clock.Advance(TimeSpan.FromHours(2));
        Assert.AreEqual(0, _service.PurgeExpiredEvents(), "終了直後: 保持期間内なので残る");

        _clock.Advance(TimeSpan.FromDays(1));
        Assert.AreEqual(1, _service.PurgeExpiredEvents(), "保持期間経過後: 削除される");
        Assert.HasCount(0, _repo.FindAll());
    }

    [TestMethod]
    public void 繰り返しイベントはシリーズ終了まで何度パージしても残る()
    {
        var rule = new RecurrenceRule(
            RecurrenceType.Weekly, 1, new LocalDateValue(2026, 6, 29),
            weekly: new WeeklyRule([Weekday.Monday]));
        var ev = CalendarEvent.CreateRecurring(
            new EventId("weekly"), new EventTitle("Weekly"), null,
            Visibility.Public, null, null, new TimeZoneId("UTC"), allDay: false,
            new RecurringEventSchedule(
                new LocalDateValue(2026, 6, 1),
                new LocalTimeValue(9, 0, 0), new LocalTimeValue(10, 0, 0),
                rule, allDay: false),
            Now);
        _repo.Save(ev);

        // 6/10: series in progress.
        Assert.AreEqual(0, _service.PurgeExpiredEvents());

        // 6/29 (last Monday) just ended.
        _clock.SetUtcNow(new DateTimeOffset(2026, 6, 29, 11, 0, 0, TimeSpan.Zero));
        Assert.AreEqual(0, _service.PurgeExpiredEvents(), "最終オカレンス終了直後は保持期間内");

        // Retention elapsed.
        _clock.SetUtcNow(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.AreEqual(1, _service.PurgeExpiredEvents());
    }

    [TestMethod]
    public void 営業日調整付きイベントは紐づく営業日カレンダーで判定される()
    {
        // Monthly on 6/15 (holiday) shifted forward to 6/16. At 6/16 the event must survive the
        // purge even though the rule's end date (6/15) has passed.
        var calendar = new BusinessCalendar(
            new BusinessCalendarId("cal1"), "JP", new TimeZoneId("UTC"),
            [Weekday.Monday, Weekday.Tuesday, Weekday.Wednesday, Weekday.Thursday, Weekday.Friday],
            [new Holiday(new LocalDateValue(2026, 6, 15), "holiday")]);
        _calendarRepo.Save(calendar);

        var rule = new RecurrenceRule(
            RecurrenceType.Monthly, 1, new LocalDateValue(2026, 6, 15),
            monthly: new DayOfMonthMonthlyRule(15),
            adjustment: new AdjustmentRule(
                AdjustmentCondition.Holiday, AdjustmentShiftUnit.BusinessDay, 1,
                new BusinessCalendarId("cal1")));
        var ev = CalendarEvent.CreateRecurring(
            new EventId("adj"), new EventTitle("Adjusted"), null,
            Visibility.Public, null, null, new TimeZoneId("UTC"), allDay: false,
            new RecurringEventSchedule(
                new LocalDateValue(2026, 6, 15),
                new LocalTimeValue(9, 0, 0), new LocalTimeValue(10, 0, 0),
                rule, allDay: false),
            Now);
        _repo.Save(ev);

        _clock.SetUtcNow(new DateTimeOffset(2026, 6, 16, 9, 30, 0, TimeSpan.Zero));
        Assert.AreEqual(0, _service.PurgeExpiredEvents(), "シフト先のオカレンス開催中は削除されない");

        _clock.SetUtcNow(new DateTimeOffset(2026, 6, 18, 0, 0, 0, TimeSpan.Zero));
        Assert.AreEqual(1, _service.PurgeExpiredEvents());
    }

    [TestMethod]
    public void 終了日なしの繰り返しイベントはパージでクラッシュせず残る()
    {
        // Startup crash regression: a weekly series without an end date is stored with the
        // 9999-12-31 sentinel and used to overflow DateOnly during expiry expansion.
        var rule = new RecurrenceRule(
            RecurrenceType.Weekly, 1, new LocalDateValue(9999, 12, 31),
            weekly: new WeeklyRule([Weekday.Monday]));
        var ev = CalendarEvent.CreateRecurring(
            new EventId("endless"), new EventTitle("Endless"), null,
            Visibility.Public, null, null, new TimeZoneId("UTC"), allDay: false,
            new RecurringEventSchedule(
                new LocalDateValue(2026, 6, 1),
                new LocalTimeValue(9, 0, 0), new LocalTimeValue(10, 0, 0),
                rule, allDay: false),
            Now);
        _repo.Save(ev);

        Assert.AreEqual(0, _service.PurgeExpiredEvents());
        Assert.IsNotNull(_repo.FindById(new EventId("endless")));
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private static CalendarEvent SingleEvent(string id, DateTimeOffset end)
    {
        return CalendarEvent.CreateSingle(
            new EventId(id), new EventTitle(id), null,
            Visibility.Public, null, null, new TimeZoneId("UTC"), allDay: false,
            new SingleEventSchedule(end.AddHours(-1), end), Now);
    }

    private sealed class InMemoryBusinessCalendarRepository : IBusinessCalendarRepository
    {
        private readonly Dictionary<string, BusinessCalendar> _store = [];
        public BusinessCalendar? FindById(BusinessCalendarId id) => _store.TryGetValue(id.Value, out var c) ? c : null;
        public IReadOnlyList<BusinessCalendar> FindAll() => [.. _store.Values];
        public void Save(BusinessCalendar calendar) => _store[calendar.Id.Value] = calendar;
        public void Delete(BusinessCalendarId id) => _store.Remove(id.Value);
    }
}
