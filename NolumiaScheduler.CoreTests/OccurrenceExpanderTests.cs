using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.Entities;
using NolumiaScheduler.Domain.Services;
using NolumiaScheduler.Domain.ValueObjects;
using Visibility = NolumiaScheduler.Domain.ValueObjects.Visibility;
using Location = NolumiaScheduler.Domain.ValueObjects.Location;

namespace NolumiaScheduler.CoreTests;

[TestClass]
public class OccurrenceExpanderTests
{
    private static readonly TimeZoneId Tokyo = new("Asia/Tokyo");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private readonly OccurrenceExpander _expander = new(new BusinessDayShiftService());

    // Builds a UTC instant from an intended local wall-clock time in the Tokyo timezone,
    // which is the timezone every event in this file is created with.
    private static DateTimeOffset Utc(int y, int mo, int d, int h, int mi) =>
        LocalSchedulePoint.StartInstant(
            new LocalDateValue(y, mo, d), new LocalTimeValue(h, mi, 0),
            Tokyo.ToTimeZoneInfo()).ToUniversalTime();

    [TestMethod]
    public void Expand_SingleEvent_InRange_ReturnsOne()
    {
        var ev = CalendarEvent.CreateSingle(
            new EventId("evt_s01"),
            new EventTitle("テスト"),
            null, Visibility.Public, null, null, Tokyo,
            new SingleEventSchedule(Utc(2026, 4, 20, 10, 0), 60),
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
            null, Visibility.Public, null, null, Tokyo,
            new SingleEventSchedule(Utc(2026, 5, 20, 10, 0), 60),
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
        // Backward-compat: events stored before the simplification may have Override exceptions.
        // The expander must still read them correctly.
        var base_ = CreateWeeklyMonday();
        var key = new OccurrenceLocalKey(new LocalDateValue(2026, 4, 27), new LocalTimeValue(10, 0, 0));
        var ov = new ExceptionOverride(title: new EventTitle("変更済み"));
        var exceptions = new List<EventException> { EventException.CreateOverride(key, ov) };
        var ev = CalendarEvent.Reconstitute(
            base_.Id, base_.Kind, base_.Title, base_.Location, base_.Visibility,
            base_.EventType, base_.Description, base_.TimeZoneId,
            base_.SingleSchedule, base_.RecurringSchedule, base_.CreatedAt, base_.UpdatedAt,
            exceptions, [], base_.Version, base_.Alarm, base_.ColorKey);

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
        // Backward-compat: events stored before the simplification may have EventMove entries.
        // The expander must still read them correctly.
        var base_ = CreateWeeklyMonday();
        var key = new OccurrenceLocalKey(new LocalDateValue(2026, 4, 27), new LocalTimeValue(10, 0, 0));
        var move = new EventMove(key, new LocalDateValue(2026, 4, 28),
            new LocalTimeValue(14, 0, 0), 60);
        var ev = CalendarEvent.Reconstitute(
            base_.Id, base_.Kind, base_.Title, base_.Location, base_.Visibility,
            base_.EventType, base_.Description, base_.TimeZoneId,
            base_.SingleSchedule, base_.RecurringSchedule, base_.CreatedAt, base_.UpdatedAt,
            [], [move], base_.Version, base_.Alarm, base_.ColorKey);

        var results = _expander.Expand(ev,
            new LocalDateValue(2026, 4, 20),
            new LocalDateValue(2026, 4, 30), null);

        Assert.HasCount(2, results);
        var moved = results.First(r => r.IsMoved);
        Assert.AreEqual(new LocalDateValue(2026, 4, 28), moved.Date);
        Assert.AreEqual(14, moved.StartTime!.Hour);
    }

    [TestMethod]
    public void Expand_WeeklyRecurring_OverriddenThenMoved_KeepsOverrideContentAtMovedDate()
    {
        // Backward-compat: legacy data with both Override and Move on the same occurrence.
        // Edit a single occurrence (override its title), then relocate it (move). The moved
        // occurrence must keep the overridden title and the new date/time.
        var base_ = CreateWeeklyMonday();
        var key = new OccurrenceLocalKey(new LocalDateValue(2026, 4, 27), new LocalTimeValue(10, 0, 0));
        var ov = new ExceptionOverride(title: new EventTitle("変更済み"));
        var exceptions = new List<EventException> { EventException.CreateOverride(key, ov) };
        var move = new EventMove(key, new LocalDateValue(2026, 4, 28), new LocalTimeValue(14, 0, 0), 60);
        var ev = CalendarEvent.Reconstitute(
            base_.Id, base_.Kind, base_.Title, base_.Location, base_.Visibility,
            base_.EventType, base_.Description, base_.TimeZoneId,
            base_.SingleSchedule, base_.RecurringSchedule, base_.CreatedAt, base_.UpdatedAt,
            exceptions, [move], base_.Version, base_.Alarm, base_.ColorKey);

        var results = _expander.Expand(ev,
            new LocalDateValue(2026, 4, 20),
            new LocalDateValue(2026, 4, 30), null);

        var moved = results.First(r => r.IsMoved);
        Assert.AreEqual(new LocalDateValue(2026, 4, 28), moved.Date);
        Assert.AreEqual(14, moved.StartTime!.Hour);
        Assert.AreEqual("変更済み", moved.Title.Value);
    }

    [TestMethod]
    public void Expand_MonthlyRecurring_NthWeekday()
    {
        // 2nd Monday of each month, starting April 2026
        var rule = new RecurrenceRule(
            RecurrenceType.Monthly, 1,
            new LocalDateValue(2026, 6, 30),
            monthly: new NthWeekdayMonthlyRule(2, Weekday.Monday));

        var schedule = new RecurringEventSchedule(
            Utc(2026, 4, 1, 10, 0),
            60,
            rule);

        var ev = CalendarEvent.CreateRecurring(
            new EventId("evt_m01"),
            new EventTitle("月例"),
            null, Visibility.Public, null, null, Tokyo,
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
        var rule = new RecurrenceRule(
            RecurrenceType.Monthly, 1,
            new LocalDateValue(2026, 5, 31),
            monthly: new DayOfMonthMonthlyRule(4),
            adjustment: new AdjustmentRule(AdjustmentDirection.Backward));

        var schedule = new RecurringEventSchedule(
            Utc(2026, 5, 1, 10, 0),
            60,
            rule);

        var ev = CalendarEvent.CreateRecurring(
            new EventId("evt_adj01"),
            new EventTitle("調整テスト"),
            null, Visibility.Public, null, null, Tokyo,
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

    // ── "N business days before a monthly anchor" (毎月15日/月末の3営業日前) ────────────

    // Mon–Fri workday calendar with the given holidays.
    private static BusinessCalendar WeekdayCalendar(params LocalDateValue[] holidays) =>
        new(new BusinessCalendarId("jp"), "JP", Tokyo,
            [Weekday.Monday, Weekday.Tuesday, Weekday.Wednesday, Weekday.Thursday, Weekday.Friday],
            holidays.Select(d => new Holiday(d, "祝")).ToList());

    private CalendarEvent MonthlyAnchorMinusBusinessDays(MonthlyRule monthly, int businessDaysBefore)
    {
        var rule = new RecurrenceRule(
            RecurrenceType.Monthly, 1,
            new LocalDateValue(2026, 6, 30),
            monthly: monthly,
            adjustment: new AdjustmentRule(
                AdjustmentCondition.Always, AdjustmentShiftUnit.BusinessDay,
                -businessDaysBefore, new BusinessCalendarId("jp")));

        return CalendarEvent.CreateRecurring(
            new EventId("evt_bd"), new EventTitle("3営業日前13時"),
            null, Visibility.Public, null, null, Tokyo,
            new RecurringEventSchedule(Utc(2026, 6, 1, 13, 0), 60, rule), Now);
    }

    [TestMethod]
    public void Expand_毎月15日の3営業日前_13時()
    {
        // June 2026: the 15th is a Monday; 3 business days before is Wed June 10 at 13:00.
        var ev = MonthlyAnchorMinusBusinessDays(new DayOfMonthMonthlyRule(15), 3);

        var results = _expander.Expand(ev,
            new LocalDateValue(2026, 6, 1), new LocalDateValue(2026, 6, 30), WeekdayCalendar());

        Assert.HasCount(1, results);
        Assert.AreEqual(new LocalDateValue(2026, 6, 10), results[0].Date);
        Assert.AreEqual(new LocalTimeValue(13, 0, 0), results[0].StartTime);
    }

    [TestMethod]
    public void Expand_毎月末の3営業日前_13時()
    {
        // June 2026 ends Tue June 30; 3 business days before is Thu June 25 at 13:00.
        var ev = MonthlyAnchorMinusBusinessDays(new LastDayOfMonthMonthlyRule(), 3);

        var results = _expander.Expand(ev,
            new LocalDateValue(2026, 6, 1), new LocalDateValue(2026, 6, 30), WeekdayCalendar());

        Assert.HasCount(1, results);
        Assert.AreEqual(new LocalDateValue(2026, 6, 25), results[0].Date);
        Assert.AreEqual(new LocalTimeValue(13, 0, 0), results[0].StartTime);
    }

    [TestMethod]
    public void Expand_3営業日前は祝日をスキップして数える()
    {
        // With Thu June 11 a holiday, counting back 3 business days from Mon June 15 skips it:
        // Fri 12, (Thu 11 holiday), Wed 10, Tue 9 → June 9.
        var ev = MonthlyAnchorMinusBusinessDays(new DayOfMonthMonthlyRule(15), 3);

        var results = _expander.Expand(ev,
            new LocalDateValue(2026, 6, 1), new LocalDateValue(2026, 6, 30),
            WeekdayCalendar(new LocalDateValue(2026, 6, 11)));

        Assert.HasCount(1, results);
        Assert.AreEqual(new LocalDateValue(2026, 6, 9), results[0].Date);
    }

    [TestMethod]
    public void Expand_YearlyRecurring_GeneratesCorrectDates()
    {
        var rule = new RecurrenceRule(
            RecurrenceType.Yearly, 1,
            new LocalDateValue(2028, 12, 31),
            yearly: new DayOfMonthYearlyRule(4, 20));

        var schedule = new RecurringEventSchedule(
            Utc(2026, 4, 20, 9, 0),
            60,
            rule);

        var ev = CalendarEvent.CreateRecurring(
            new EventId("evt_y01"),
            new EventTitle("年次レビュー"),
            null, Visibility.Public, null, null, Tokyo,
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
        var rule = new RecurrenceRule(
            RecurrenceType.Weekly, 1,
            new LocalDateValue(2026, 4, 30),
            weekly: new WeeklyRule([Weekday.Monday]));

        // An "all-day" recurrence is now modeled as start 00:00 + 1440-minute duration.
        var schedule = new RecurringEventSchedule(
            Utc(2026, 4, 20, 0, 0), 24 * 60,
            rule);

        var ev = CalendarEvent.CreateRecurring(
            new EventId("evt_allday01"),
            new EventTitle("終日イベント"),
            null, Visibility.Public, null, null, Tokyo,
            schedule, Now);

        var results = _expander.Expand(ev,
            new LocalDateValue(2026, 4, 20),
            new LocalDateValue(2026, 4, 30), null);

        // Mondays in [4/20, 4/30]: 4/20, 4/27
        Assert.HasCount(2, results);
        Assert.IsTrue(results.All(r => r.StartMinuteOfDay == 0 && r.DurationMinutes == 24 * 60));
        Assert.IsTrue(results.All(r => r.StartTime.Equals(new LocalTimeValue(0, 0, 0))));
    }

    [TestMethod]
    public void Expand_WeeklyRecurring_OutsideEndDate_ReturnsEmpty()
    {
        var rule = new RecurrenceRule(
            RecurrenceType.Weekly, 1,
            new LocalDateValue(2026, 4, 26),
            weekly: new WeeklyRule([Weekday.Monday]));

        var schedule = new RecurringEventSchedule(
            Utc(2026, 4, 20, 10, 0),
            60,
            rule);

        var ev = CalendarEvent.CreateRecurring(
            new EventId("evt_w02"),
            new EventTitle("期限切れ会議"),
            null, Visibility.Public, null, null, Tokyo,
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
        var rule = new RecurrenceRule(
            RecurrenceType.Monthly, 1,
            new LocalDateValue(2026, 5, 31),
            monthly: new DayOfMonthMonthlyRule(1),
            adjustment: new AdjustmentRule(AdjustmentDirection.Backward));

        var schedule = new RecurringEventSchedule(
            Utc(2026, 5, 1, 10, 0),
            60,
            rule);

        var ev = CalendarEvent.CreateRecurring(
            new EventId("evt_adj02"),
            new EventTitle("非祝日テスト"),
            null, Visibility.Public, null, null, Tokyo,
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

    [TestMethod]
    public void Expand_範囲外の日付から範囲内へ移動されたオカレンスも展開される()
    {
        // Backward-compat: original occurrence 4/27 (outside the queried window) moved into 5/13:
        // the occurrence must show up when expanding May even though its candidate date is in April.
        var base_ = CreateWeeklyMonday();
        var key = new OccurrenceLocalKey(new LocalDateValue(2026, 4, 27), new LocalTimeValue(10, 0, 0));
        var move = new EventMove(key, new LocalDateValue(2026, 5, 13), new LocalTimeValue(14, 0, 0), 60);
        var ev = CalendarEvent.Reconstitute(
            base_.Id, base_.Kind, base_.Title, base_.Location, base_.Visibility,
            base_.EventType, base_.Description, base_.TimeZoneId,
            base_.SingleSchedule, base_.RecurringSchedule, base_.CreatedAt, base_.UpdatedAt,
            [], [move], base_.Version, base_.Alarm, base_.ColorKey);

        var results = _expander.Expand(ev,
            new LocalDateValue(2026, 5, 10),
            new LocalDateValue(2026, 5, 16), null);

        var moved = results.Single(r => r.IsMoved);
        Assert.AreEqual(new LocalDateValue(2026, 5, 13), moved.Date);
        Assert.AreEqual(key, moved.SeriesKey);
    }

    [TestMethod]
    public void Expand_週次_日曜が最終週でend超えても他曜日の発生を打ち切らない()
    {
        // Weekdays: Sunday + Wednesday.  EndDate = 2026-05-07 (Thursday).
        // Week starting Mon 2026-05-04: Sunday candidate = 2026-05-10 > end.
        // The bug caused yield break there, skipping Wednesday 2026-05-06 which IS within range.
        var rule = new RecurrenceRule(
            RecurrenceType.Weekly, 1,
            new LocalDateValue(2026, 5, 7),
            weekly: new WeeklyRule([Weekday.Sunday, Weekday.Wednesday]));

        var schedule = new RecurringEventSchedule(
            Utc(2026, 5, 1, 10, 0),
            60,
            rule);

        var ev = CalendarEvent.CreateRecurring(
            new EventId("evt_sun_bug"),
            new EventTitle("日水テスト"),
            null, Visibility.Public, null, null, Tokyo,
            schedule, Now);

        var results = _expander.Expand(ev,
            new LocalDateValue(2026, 5, 1),
            new LocalDateValue(2026, 5, 7), null);

        // Expected: Sun 5/3, Wed 5/6 (Sun 5/10 is beyond end and must NOT terminate Wed)
        var dates = results.Select(r => r.Date).ToList();
        CollectionAssert.Contains(dates, new LocalDateValue(2026, 5, 3));
        CollectionAssert.Contains(dates, new LocalDateValue(2026, 5, 6));
        Assert.IsFalse(dates.Any(d => d.Equals(new LocalDateValue(2026, 5, 10))),
            "Sunday 5/10 is past end date and must not appear");
    }

    // ── calendar upper-bound (year 9999) guards ───────────────────────────
    // "No end date" series persist their end date as 9999-12-31, so expansion must stop
    // cleanly at DateOnly.MaxValue instead of overflowing in AddDays/AddMonths.

    [TestMethod]
    public void Expand_Weekly_暦の上限9999年末まで展開してもオーバーフローしない()
    {
        var rule = new RecurrenceRule(
            RecurrenceType.Weekly, 1,
            new LocalDateValue(9999, 12, 31),
            weekly: new WeeklyRule([Weekday.Monday]));
        var ev = CreateRecurring("evt_max_w", new LocalDateValue(9999, 12, 1), rule);

        var results = _expander.Expand(ev,
            new LocalDateValue(9999, 12, 1),
            new LocalDateValue(9999, 12, 31), null);

        // December 9999: Mondays are 6, 13, 20, 27 (12/31 is a Friday).
        Assert.HasCount(4, results);
        Assert.AreEqual(new LocalDateValue(9999, 12, 27), results[^1].Date);
    }

    [TestMethod]
    public void Expand_Monthly_暦の上限9999年末まで展開してもオーバーフローしない()
    {
        var rule = new RecurrenceRule(
            RecurrenceType.Monthly, 1,
            new LocalDateValue(9999, 12, 31),
            monthly: new DayOfMonthMonthlyRule(15));
        var ev = CreateRecurring("evt_max_m", new LocalDateValue(9999, 10, 1), rule);

        var results = _expander.Expand(ev,
            new LocalDateValue(9999, 10, 1),
            new LocalDateValue(9999, 12, 31), null);

        Assert.HasCount(3, results);
        Assert.AreEqual(new LocalDateValue(9999, 12, 15), results[^1].Date);
    }

    [TestMethod]
    public void Expand_Yearly_暦の上限9999年まで展開してもオーバーフローしない()
    {
        var rule = new RecurrenceRule(
            RecurrenceType.Yearly, 1,
            new LocalDateValue(9999, 12, 31),
            yearly: new DayOfMonthYearlyRule(12, 31));
        var ev = CreateRecurring("evt_max_y", new LocalDateValue(9997, 1, 1), rule);

        var results = _expander.Expand(ev,
            new LocalDateValue(9997, 1, 1),
            new LocalDateValue(9999, 12, 31), null);

        Assert.HasCount(3, results);
        Assert.AreEqual(new LocalDateValue(9999, 12, 31), results[^1].Date);
    }

    private static CalendarEvent CreateRecurring(string id, LocalDateValue startDate, RecurrenceRule rule)
    {
        var schedule = new RecurringEventSchedule(
            Utc(startDate.Year, startDate.Month, startDate.Day, 10, 0),
            60,
            rule);

        return CalendarEvent.CreateRecurring(
            new EventId(id),
            new EventTitle("上限テスト"),
            null, Visibility.Public, null, null, Tokyo,
            schedule, Now);
    }

    private static CalendarEvent CreateWeeklyMonday()
    {
        var rule = new RecurrenceRule(
            RecurrenceType.Weekly, 1,
            new LocalDateValue(2026, 12, 31),
            weekly: new WeeklyRule([Weekday.Monday]));

        var schedule = new RecurringEventSchedule(
            Utc(2026, 4, 20, 10, 0),
            60,
            rule);

        return CalendarEvent.CreateRecurring(
            new EventId("evt_w01"),
            new EventTitle("週次会議"),
            new Location("会議室A"),
            Visibility.Public, null, null, Tokyo,
            schedule, Now);
    }
}
