using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.Entities;
using NolumiaScheduler.Domain.Services;
using NolumiaScheduler.Domain.ValueObjects;
using Visibility = NolumiaScheduler.Domain.ValueObjects.Visibility;

namespace NolumiaScheduler.CoreTests;

/// <summary>
/// State transitions of "an event's lifetime is over" driven purely by the reference time:
/// the same event flips from alive to expired as time passes its final occurrence end.
/// </summary>
[TestClass]
public class EventExpirationServiceTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly EventExpirationService _service = new(new OccurrenceExpander(new BusinessDayShiftService()));

    // ── 単発イベント ───────────────────────────────────────────────────────

    [TestMethod]
    public void 単発_終了前は期限切れではない()
    {
        var ev = SingleEvent(end: new DateTimeOffset(2026, 6, 10, 11, 0, 0, TimeSpan.Zero));

        Assert.IsFalse(_service.IsExpired(ev, null, new DateTimeOffset(2026, 6, 10, 10, 59, 59, TimeSpan.Zero)));
    }

    [TestMethod]
    public void 単発_終了時刻ちょうどで期限切れに遷移する()
    {
        var end = new DateTimeOffset(2026, 6, 10, 11, 0, 0, TimeSpan.Zero);
        var ev = SingleEvent(end);

        Assert.IsFalse(_service.IsExpired(ev, null, end.AddSeconds(-1)), "1秒前はまだ有効");
        Assert.IsTrue(_service.IsExpired(ev, null, end), "終了時刻ちょうどで期限切れ");
        Assert.IsTrue(_service.IsExpired(ev, null, end.AddDays(3)), "終了後はずっと期限切れ");
    }

    [TestMethod]
    public void 単発_最終終了時刻はスケジュールのEndそのもの()
    {
        var end = new DateTimeOffset(2026, 6, 10, 11, 0, 0, TimeSpan.FromHours(9));
        var ev = SingleEvent(end, timeZone: "Asia/Tokyo");

        Assert.AreEqual(end, _service.GetLastOccurrenceEnd(ev, null));
    }

    [TestMethod]
    public void 単発終日_当日中は有効で翌日0時に期限切れへ遷移する()
    {
        // All-day single events are stored as [00:00, next day 00:00).
        var start = new DateTimeOffset(2026, 6, 10, 0, 0, 0, TimeSpan.Zero);
        var ev = CalendarEvent.CreateSingle(
            new EventId("allday"), new EventTitle("AllDay"), null,
            Visibility.Public, null, null, new TimeZoneId("UTC"), allDay: true,
            new SingleEventSchedule(start, start.AddDays(1)), CreatedAt);

        Assert.IsFalse(_service.IsExpired(ev, null, new DateTimeOffset(2026, 6, 10, 23, 59, 59, TimeSpan.Zero)));
        Assert.IsTrue(_service.IsExpired(ev, null, new DateTimeOffset(2026, 6, 11, 0, 0, 0, TimeSpan.Zero)));
    }

    // ── 繰り返しイベント ────────────────────────────────────────────────────

    [TestMethod]
    public void 繰り返し_最終オカレンス終了前は有効_終了後は期限切れ()
    {
        // Weekly Monday 09:00-10:00, end date 2026-06-15 (Monday) → last end 6/15 10:00 UTC.
        var ev = WeeklyMondayEvent(endDate: new LocalDateValue(2026, 6, 15));

        var lastEnd = new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);
        Assert.AreEqual(lastEnd, _service.GetLastOccurrenceEnd(ev, null));
        Assert.IsFalse(_service.IsExpired(ev, null, lastEnd.AddMinutes(-1)));
        Assert.IsTrue(_service.IsExpired(ev, null, lastEnd));
    }

    [TestMethod]
    public void 繰り返し_期限はルール終了日より後ろへ移動したオカレンスまで延びる()
    {
        // Last regular occurrence is 6/15, but that occurrence is moved to 7/20.
        var ev = WeeklyMondayEvent(endDate: new LocalDateValue(2026, 6, 15));
        ev.MoveOccurrence(new EventMove(
            new OccurrenceLocalKey(new LocalDateValue(2026, 6, 15), new LocalTimeValue(9, 0, 0)),
            new LocalDateValue(2026, 7, 20),
            new LocalTimeValue(9, 0, 0),
            new LocalTimeValue(10, 0, 0)), CreatedAt);

        var movedEnd = new DateTimeOffset(2026, 7, 20, 10, 0, 0, TimeSpan.Zero);
        Assert.AreEqual(movedEnd, _service.GetLastOccurrenceEnd(ev, null));
        Assert.IsFalse(_service.IsExpired(ev, null, new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)),
            "ルール上の終了日を過ぎても移動先が未来なら有効");
        Assert.IsTrue(_service.IsExpired(ev, null, movedEnd));
    }

    [TestMethod]
    public void 繰り返し_全オカレンスをスキップすると即座に期限切れ扱い()
    {
        // One-occurrence series (6/8 only) whose only occurrence is skipped.
        var ev = WeeklyMondayEvent(startDate: new LocalDateValue(2026, 6, 8), endDate: new LocalDateValue(2026, 6, 8));
        ev.SkipOccurrence(
            new OccurrenceLocalKey(new LocalDateValue(2026, 6, 8), new LocalTimeValue(9, 0, 0)),
            CreatedAt);

        Assert.IsNull(_service.GetLastOccurrenceEnd(ev, null));
        Assert.IsTrue(_service.IsExpired(ev, null, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            "発生し得ないイベントは常に期限切れ");
    }

    [TestMethod]
    public void 繰り返し終日_最終日の終わりまで有効()
    {
        var rule = new RecurrenceRule(
            RecurrenceType.Weekly, 1, new LocalDateValue(2026, 6, 15),
            weekly: new WeeklyRule([Weekday.Monday]));
        var ev = CalendarEvent.CreateRecurring(
            new EventId("rec-allday"), new EventTitle("Rec AllDay"), null,
            Visibility.Public, null, null, new TimeZoneId("UTC"), allDay: true,
            new RecurringEventSchedule(new LocalDateValue(2026, 6, 1), null, null, rule, allDay: true),
            CreatedAt);

        Assert.IsFalse(_service.IsExpired(ev, null, new DateTimeOffset(2026, 6, 15, 23, 0, 0, TimeSpan.Zero)));
        Assert.IsTrue(_service.IsExpired(ev, null, new DateTimeOffset(2026, 6, 16, 0, 0, 0, TimeSpan.Zero)));
    }

    [TestMethod]
    public void タイムゾーン_ローカル終了時刻がUTC基準で正しく比較される()
    {
        // Tokyo 10:00 end = 01:00 UTC.
        var ev = WeeklyMondayEvent(endDate: new LocalDateValue(2026, 6, 15), timeZone: "Asia/Tokyo");

        Assert.IsFalse(_service.IsExpired(ev, null, new DateTimeOffset(2026, 6, 15, 0, 59, 0, TimeSpan.Zero)));
        Assert.IsTrue(_service.IsExpired(ev, null, new DateTimeOffset(2026, 6, 15, 1, 0, 0, TimeSpan.Zero)));
    }

    [TestMethod]
    public void 営業日調整_休日から後ろ倒しされたオカレンスの分だけ期限が延びる()
    {
        // Monthly on the 15th ending 2026-06-15; 6/15 (Monday) is a holiday, so the occurrence
        // shifts forward to 6/16 and the event must stay alive until 6/16 10:00.
        var calendar = new BusinessCalendar(
            new BusinessCalendarId("cal1"), "JP", new TimeZoneId("UTC"),
            [Weekday.Monday, Weekday.Tuesday, Weekday.Wednesday, Weekday.Thursday, Weekday.Friday],
            [new Holiday(new LocalDateValue(2026, 6, 15), "holiday")]);

        var rule = new RecurrenceRule(
            RecurrenceType.Monthly, 1, new LocalDateValue(2026, 6, 15),
            monthly: new DayOfMonthMonthlyRule(15),
            adjustment: new AdjustmentRule(AdjustmentDirection.Forward));
        var ev = CalendarEvent.CreateRecurring(
            new EventId("adj"), new EventTitle("Adjusted"), null,
            Visibility.Public, null, null, new TimeZoneId("UTC"), allDay: false,
            new RecurringEventSchedule(
                new LocalDateValue(2026, 6, 15), new LocalTimeValue(9, 0, 0), new LocalTimeValue(10, 0, 0),
                rule, allDay: false),
            CreatedAt);

        Assert.AreEqual(
            new DateTimeOffset(2026, 6, 16, 10, 0, 0, TimeSpan.Zero),
            _service.GetLastOccurrenceEnd(ev, calendar));
        Assert.IsFalse(_service.IsExpired(ev, calendar, new DateTimeOffset(2026, 6, 16, 9, 0, 0, TimeSpan.Zero)),
            "シフト先のオカレンスが終わるまでは有効");
        Assert.IsTrue(_service.IsExpired(ev, calendar, new DateTimeOffset(2026, 6, 16, 10, 0, 0, TimeSpan.Zero)));
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private static CalendarEvent SingleEvent(DateTimeOffset end, string timeZone = "UTC")
    {
        return CalendarEvent.CreateSingle(
            new EventId("single"), new EventTitle("Single"), null,
            Visibility.Public, null, null, new TimeZoneId(timeZone), allDay: false,
            new SingleEventSchedule(end.AddHours(-1), end), CreatedAt);
    }

    private static CalendarEvent WeeklyMondayEvent(
        LocalDateValue? startDate = null,
        LocalDateValue? endDate = null,
        string timeZone = "UTC")
    {
        var rule = new RecurrenceRule(
            RecurrenceType.Weekly, 1, endDate ?? new LocalDateValue(2026, 12, 28),
            weekly: new WeeklyRule([Weekday.Monday]));
        return CalendarEvent.CreateRecurring(
            new EventId("weekly"), new EventTitle("Weekly"), null,
            Visibility.Public, null, null, new TimeZoneId(timeZone), allDay: false,
            new RecurringEventSchedule(
                startDate ?? new LocalDateValue(2026, 6, 1),
                new LocalTimeValue(9, 0, 0), new LocalTimeValue(10, 0, 0),
                rule, allDay: false),
            CreatedAt);
    }
}
