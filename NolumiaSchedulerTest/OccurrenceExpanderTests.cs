using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.Entities;
using NolumiaScheduler.Domain.Services;
using NolumiaScheduler.Domain.ValueObjects;
using Visibility = NolumiaScheduler.Domain.ValueObjects.Visibility;
using Location = NolumiaScheduler.Domain.ValueObjects.Location;

namespace NolumiaSchedulerTest;

[TestClass]
public class OccurrenceExpanderTests
{
    private static readonly TimeZoneId Tokyo = new("Asia/Tokyo");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private readonly OccurrenceExpander _expander = new(new BusinessDayShiftService());

    [TestMethod]
    public void Expand_SingleEvent_InRange_ReturnsOne()
    {
        var ev = CalendarEvent.CreateSingle(
            new EventId("evt_s01"),
            new EventTitle("テスト"),
            null, Visibility.Public, null, null, Tokyo, false,
            new SingleEventSchedule(
                new DateTimeOffset(2026, 4, 20, 10, 0, 0, TimeSpan.FromHours(9)),
                new DateTimeOffset(2026, 4, 20, 11, 0, 0, TimeSpan.FromHours(9))),
            Now);

        var results = _expander.Expand(ev,
            new LocalDateValue(2026, 4, 1),
            new LocalDateValue(2026, 4, 30), null);

        Assert.HasCount(1, results);
        Assert.AreEqual("テスト", results[0].Title.Value);
    }

    [TestMethod]
    public void Expand_SingleEvent_OutOfRange_ReturnsEmpty()
    {
        var ev = CalendarEvent.CreateSingle(
            new EventId("evt_s02"),
            new EventTitle("テスト"),
            null, Visibility.Public, null, null, Tokyo, false,
            new SingleEventSchedule(
                new DateTimeOffset(2026, 5, 20, 10, 0, 0, TimeSpan.FromHours(9)),
                new DateTimeOffset(2026, 5, 20, 11, 0, 0, TimeSpan.FromHours(9))),
            Now);

        var results = _expander.Expand(ev,
            new LocalDateValue(2026, 4, 1),
            new LocalDateValue(2026, 4, 30), null);

        Assert.IsEmpty(results);
    }

    [TestMethod]
    public void Expand_WeeklyRecurring_GeneratesCorrectCount()
    {
        var ev = CreateWeeklyMonday();

        // April 2026: Mondays are 6,13,20,27 - startDate is 20, so 20 and 27
        var results = _expander.Expand(ev,
            new LocalDateValue(2026, 4, 20),
            new LocalDateValue(2026, 4, 30), null);

        Assert.HasCount(2, results);
    }

    [TestMethod]
    public void Expand_WeeklyRecurring_WithSkip_ExcludesSkipped()
    {
        var ev = CreateWeeklyMonday();
        var key = new OccurrenceLocalKey(new LocalDateValue(2026, 4, 27), new LocalTimeValue(10, 0, 0));
        ev.SkipOccurrence(key, Now);

        var results = _expander.Expand(ev,
            new LocalDateValue(2026, 4, 20),
            new LocalDateValue(2026, 4, 30), null);

        Assert.HasCount(1, results);
        Assert.AreEqual(new LocalDateValue(2026, 4, 20), results[0].Date);
    }

    [TestMethod]
    public void Expand_WeeklyRecurring_WithOverride_ReturnsOverriddenValues()
    {
        var ev = CreateWeeklyMonday();
        var key = new OccurrenceLocalKey(new LocalDateValue(2026, 4, 27), new LocalTimeValue(10, 0, 0));
        var ov = new ExceptionOverride(title: new EventTitle("変更済み"));
        ev.OverrideOccurrence(key, ov, Now);

        var results = _expander.Expand(ev,
            new LocalDateValue(2026, 4, 20),
            new LocalDateValue(2026, 4, 30), null);

        var overridden = results.First(r => r.Date.Equals(new LocalDateValue(2026, 4, 27)));
        Assert.AreEqual("変更済み", overridden.Title.Value);
        Assert.IsTrue(overridden.IsOverridden);
    }

    [TestMethod]
    public void Expand_WeeklyRecurring_WithMove_ShowsMovedDate()
    {
        var ev = CreateWeeklyMonday();
        var key = new OccurrenceLocalKey(new LocalDateValue(2026, 4, 27), new LocalTimeValue(10, 0, 0));
        var move = new EventMove(key, new LocalDateValue(2026, 4, 28),
            new LocalTimeValue(14, 0, 0), new LocalTimeValue(15, 0, 0));
        ev.MoveOccurrence(move, Now);

        var results = _expander.Expand(ev,
            new LocalDateValue(2026, 4, 20),
            new LocalDateValue(2026, 4, 30), null);

        Assert.HasCount(2, results);
        var moved = results.First(r => r.IsMoved);
        Assert.AreEqual(new LocalDateValue(2026, 4, 28), moved.Date);
        Assert.AreEqual(14, moved.StartTime!.Hour);
    }

    [TestMethod]
    public void Expand_MonthlyRecurring_NthWeekday()
    {
        // 2nd Monday of each month, starting April 2026
        var startDate = new LocalDateValue(2026, 4, 1);
        var rule = new RecurrenceRule(
            RecurrenceType.Monthly, 1,
            new LocalDateValue(2026, 6, 30),
            monthly: new NthWeekdayMonthlyRule(2, Weekday.Monday));

        var schedule = new RecurringEventSchedule(
            startDate,
            new LocalTimeValue(10, 0, 0),
            new LocalTimeValue(11, 0, 0),
            rule, false);

        var ev = CalendarEvent.CreateRecurring(
            new EventId("evt_m01"),
            new EventTitle("月例"),
            null, Visibility.Public, null, null, Tokyo, false,
            schedule, Now);

        var results = _expander.Expand(ev,
            new LocalDateValue(2026, 4, 1),
            new LocalDateValue(2026, 6, 30), null);

        // April 2nd Monday = 13, May = 11, June = 8
        Assert.HasCount(3, results);
        Assert.AreEqual(new LocalDateValue(2026, 4, 13), results[0].Date);
        Assert.AreEqual(new LocalDateValue(2026, 5, 11), results[1].Date);
        Assert.AreEqual(new LocalDateValue(2026, 6, 8), results[2].Date);
    }

    [TestMethod]
    public void Expand_WithBusinessDayAdjustment_ShiftsHoliday()
    {
        var startDate = new LocalDateValue(2026, 5, 1);
        var rule = new RecurrenceRule(
            RecurrenceType.Monthly, 1,
            new LocalDateValue(2026, 5, 31),
            monthly: new DayOfMonthMonthlyRule(4),
            adjustment: new AdjustmentRule(AdjustmentDirection.Backward));

        var schedule = new RecurringEventSchedule(
            startDate,
            new LocalTimeValue(10, 0, 0),
            new LocalTimeValue(11, 0, 0),
            rule, false);

        var ev = CalendarEvent.CreateRecurring(
            new EventId("evt_adj01"),
            new EventTitle("調整テスト"),
            null, Visibility.Public, null, null, Tokyo, false,
            schedule, Now);

        // May 4 is a holiday in our test calendar
        var calendar = new BusinessCalendar(
            new BusinessCalendarId("jp_test"),
            "Test",
            Tokyo,
            [Weekday.Monday, Weekday.Tuesday, Weekday.Wednesday, Weekday.Thursday, Weekday.Friday],
            [new Holiday(new LocalDateValue(2026, 5, 4), "みどりの日"),
             new Holiday(new LocalDateValue(2026, 5, 3), "憲法記念日")]);

        var results = _expander.Expand(ev,
            new LocalDateValue(2026, 5, 1),
            new LocalDateValue(2026, 5, 31), calendar);

        // May 4 is holiday, May 3 is holiday, so should shift backward to May 1 (Friday)
        Assert.HasCount(1, results);
        Assert.AreEqual(new LocalDateValue(2026, 5, 1), results[0].Date);
    }

    [TestMethod]
    public void Expand_YearlyRecurring_GeneratesCorrectDates()
    {
        var startDate = new LocalDateValue(2026, 4, 20);
        var rule = new RecurrenceRule(
            RecurrenceType.Yearly, 1,
            new LocalDateValue(2028, 12, 31),
            yearly: new DayOfMonthYearlyRule(4, 20));

        var schedule = new RecurringEventSchedule(
            startDate,
            new LocalTimeValue(9, 0, 0),
            new LocalTimeValue(10, 0, 0),
            rule, false);

        var ev = CalendarEvent.CreateRecurring(
            new EventId("evt_y01"),
            new EventTitle("年次レビュー"),
            null, Visibility.Public, null, null, Tokyo, false,
            schedule, Now);

        var results = _expander.Expand(ev,
            new LocalDateValue(2026, 1, 1),
            new LocalDateValue(2028, 12, 31), null);

        Assert.HasCount(3, results);
        Assert.AreEqual(new LocalDateValue(2026, 4, 20), results[0].Date);
        Assert.AreEqual(new LocalDateValue(2027, 4, 20), results[1].Date);
        Assert.AreEqual(new LocalDateValue(2028, 4, 20), results[2].Date);
        Assert.IsTrue(results.All(r => r.StartTime!.Equals(new LocalTimeValue(9, 0, 0))));
    }

    [TestMethod]
    public void Expand_RecurringAllDay_HasNullTimes()
    {
        var startDate = new LocalDateValue(2026, 4, 20);
        var rule = new RecurrenceRule(
            RecurrenceType.Weekly, 1,
            new LocalDateValue(2026, 4, 30),
            weekly: new WeeklyRule([Weekday.Monday]));

        var schedule = new RecurringEventSchedule(
            startDate,
            null, null,
            rule, allDay: true);

        var ev = CalendarEvent.CreateRecurring(
            new EventId("evt_allday01"),
            new EventTitle("終日イベント"),
            null, Visibility.Public, null, null, Tokyo, true,
            schedule, Now);

        var results = _expander.Expand(ev,
            new LocalDateValue(2026, 4, 20),
            new LocalDateValue(2026, 4, 30), null);

        // Mondays in [4/20, 4/30]: 4/20, 4/27
        Assert.HasCount(2, results);
        Assert.IsTrue(results.All(r => r.AllDay));
        Assert.IsTrue(results.All(r => r.StartTime is null));
        Assert.IsTrue(results.All(r => r.EndTime is null));
    }

    [TestMethod]
    public void Expand_WeeklyRecurring_OutsideEndDate_ReturnsEmpty()
    {
        var startDate = new LocalDateValue(2026, 4, 20);
        var rule = new RecurrenceRule(
            RecurrenceType.Weekly, 1,
            new LocalDateValue(2026, 4, 26),
            weekly: new WeeklyRule([Weekday.Monday]));

        var schedule = new RecurringEventSchedule(
            startDate,
            new LocalTimeValue(10, 0, 0),
            new LocalTimeValue(11, 0, 0),
            rule, false);

        var ev = CalendarEvent.CreateRecurring(
            new EventId("evt_w02"),
            new EventTitle("期限切れ会議"),
            null, Visibility.Public, null, null, Tokyo, false,
            schedule, Now);

        // Query after rule.EndDate
        var results = _expander.Expand(ev,
            new LocalDateValue(2026, 4, 27),
            new LocalDateValue(2026, 5, 31), null);

        Assert.IsEmpty(results);
    }

    [TestMethod]
    public void Expand_WithAdjustment_NonHolidayDate_IsNotShifted()
    {
        var startDate = new LocalDateValue(2026, 5, 1);
        var rule = new RecurrenceRule(
            RecurrenceType.Monthly, 1,
            new LocalDateValue(2026, 5, 31),
            monthly: new DayOfMonthMonthlyRule(1),
            adjustment: new AdjustmentRule(AdjustmentDirection.Backward));

        var schedule = new RecurringEventSchedule(
            startDate,
            new LocalTimeValue(10, 0, 0),
            new LocalTimeValue(11, 0, 0),
            rule, false);

        var ev = CalendarEvent.CreateRecurring(
            new EventId("evt_adj02"),
            new EventTitle("非祝日テスト"),
            null, Visibility.Public, null, null, Tokyo, false,
            schedule, Now);

        // May 1 is not a holiday in this calendar
        var calendar = new BusinessCalendar(
            new BusinessCalendarId("jp_test2"),
            "Test2",
            Tokyo,
            [Weekday.Monday, Weekday.Tuesday, Weekday.Wednesday, Weekday.Thursday, Weekday.Friday],
            [new Holiday(new LocalDateValue(2026, 5, 4), "みどりの日")]);

        var results = _expander.Expand(ev,
            new LocalDateValue(2026, 5, 1),
            new LocalDateValue(2026, 5, 31), calendar);

        Assert.HasCount(1, results);
        Assert.AreEqual(new LocalDateValue(2026, 5, 1), results[0].Date);
    }

    private static CalendarEvent CreateWeeklyMonday()
    {
        var startDate = new LocalDateValue(2026, 4, 20);
        var rule = new RecurrenceRule(
            RecurrenceType.Weekly, 1,
            new LocalDateValue(2026, 12, 31),
            weekly: new WeeklyRule([Weekday.Monday]));

        var schedule = new RecurringEventSchedule(
            startDate,
            new LocalTimeValue(10, 0, 0),
            new LocalTimeValue(11, 0, 0),
            rule, false);

        return CalendarEvent.CreateRecurring(
            new EventId("evt_w01"),
            new EventTitle("週次会議"),
            new Location("会議室A"),
            Visibility.Public, null, null, Tokyo, false,
            schedule, Now);
    }
}
