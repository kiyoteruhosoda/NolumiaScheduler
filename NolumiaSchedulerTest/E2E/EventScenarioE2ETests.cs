using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.Services;
using NolumiaScheduler.Domain.ValueObjects;
using DomainVisibility = NolumiaScheduler.Domain.ValueObjects.Visibility;

namespace NolumiaSchedulerTest.E2E;

[TestClass]
public class EventScenarioE2ETests
{
    private const string SingleNormalJson = "単発・通常イベント.json";
    private const string SingleAllDayJson = "単発・終日イベント.json";
    private const string SingleCrossDayJson = "単発・日またぎイベント.json";
    private const string WeeklyMondayJson = "毎週月曜 1000-1100.json";
    private const string RecurringAllDayJson = "繰り返し・終日イベント.json";
    private const string YearlyApril20Json = "毎年4月20日.json";
    private const string MonthlySecondMondayJson = "毎月第2月曜、祝日なら前営業日へ.json";
    private const string SkipOnceJson = "この回だけスキップ.json";
    private const string OverrideOnceJson = "この回だけ時間変更.json";
    private const string MoveOnceJson = "この回だけ移動.json";
    private const string BusinessCalendarJson = "営業日カレンダーサンプル.json";

    private readonly OccurrenceExpander _expander = new(new BusinessDayShiftService());

    // All-day in the UTC model: the anchor resolves to local midnight and spans a full day.
    private static bool IsAllDay(DateTimeOffset anchorUtc, int durationMinutes, TimeZoneId tz)
        => LocalSchedulePoint.LocalTimeOf(anchorUtc, tz.ToTimeZoneInfo()).Equals(new LocalTimeValue(0, 0, 0))
           && durationMinutes == 24 * 60;

    // ---------- Single events ----------

    [TestMethod]
    public void Scenario_SingleNormalEvent_LoadsAndExpandsToOneOccurrence()
    {
        var ev = JsonScenarioLoader.LoadEvent(SingleNormalJson);

        Assert.AreEqual("evt_single_001", ev.Id.Value);
        Assert.AreEqual("顧客訪問", ev.Title.Value);
        Assert.AreEqual("東京本社", ev.Location!.Value);
        Assert.AreEqual(DomainVisibility.Public, ev.Visibility);
        Assert.IsTrue(ev.IsSingle());
        // Not all-day: derived from the duration model (all-day is 00:00 + 1440).
        var single = ev.SingleSchedule!;
        Assert.IsFalse(IsAllDay(single.StartUtc, single.DurationMinutes, ev.TimeZoneId));

        var results = _expander.Expand(ev,
            new LocalDateValue(2026, 4, 1),
            new LocalDateValue(2026, 4, 30),
            null);

        Assert.HasCount(1, results);
        var occ = results[0];
        Assert.AreEqual(new LocalDateValue(2026, 4, 21), occ.Date);
        Assert.AreEqual(new LocalTimeValue(14, 0, 0), occ.StartTime);
        // End 15:30 = start (14:00 = 840) + 90 minutes.
        Assert.AreEqual(90, occ.DurationMinutes);
        // End 15:30 = start (14:00 = 840) + 90 minutes.
        Assert.AreEqual((15 * 60) + 30, (occ.StartMinuteOfDay + occ.DurationMinutes) % (24 * 60));
        Assert.IsFalse(occ.StartMinuteOfDay == 0 && occ.DurationMinutes == 24 * 60);
    }

    [TestMethod]
    public void Scenario_SingleNormalEvent_OutOfRange_ReturnsEmpty()
    {
        var ev = JsonScenarioLoader.LoadEvent(SingleNormalJson);

        var results = _expander.Expand(ev,
            new LocalDateValue(2026, 5, 1),
            new LocalDateValue(2026, 5, 31),
            null);

        Assert.IsEmpty(results);
    }

    [TestMethod]
    public void Scenario_SingleAllDayEvent_LoadsWithAllDayFlag()
    {
        var ev = JsonScenarioLoader.LoadEvent(SingleAllDayJson);

        Assert.AreEqual("evt_single_003", ev.Id.Value);
        Assert.IsTrue(ev.IsSingle());
        // All-day in the duration model: start 00:00 + 1440 minutes.
        var single = ev.SingleSchedule!;
        Assert.IsTrue(IsAllDay(single.StartUtc, single.DurationMinutes, ev.TimeZoneId));
        Assert.IsNull(ev.Location);

        var results = _expander.Expand(ev,
            new LocalDateValue(2026, 5, 1),
            new LocalDateValue(2026, 5, 31),
            null);

        Assert.HasCount(1, results);
        var occ = results[0];
        Assert.AreEqual(new LocalDateValue(2026, 5, 1), occ.Date);
        Assert.IsTrue(occ.StartMinuteOfDay == 0 && occ.DurationMinutes == 24 * 60);
        Assert.AreEqual("有給休暇", occ.Title.Value);
    }

    [TestMethod]
    public void Scenario_SingleCrossDayEvent_UsesStartDateInTimezone()
    {
        var ev = JsonScenarioLoader.LoadEvent(SingleCrossDayJson);

        var single = ev.SingleSchedule!;
        Assert.IsFalse(IsAllDay(single.StartUtc, single.DurationMinutes, ev.TimeZoneId));
        Assert.IsTrue(ev.IsSingle());

        var results = _expander.Expand(ev,
            new LocalDateValue(2026, 4, 1),
            new LocalDateValue(2026, 4, 30),
            null);

        Assert.HasCount(1, results);
        var occ = results[0];
        // start in JST is 2026-04-25 23:00
        Assert.AreEqual(new LocalDateValue(2026, 4, 25), occ.Date);
        Assert.AreEqual(new LocalTimeValue(23, 0, 0), occ.StartTime);
        // end in JST is 2026-04-26 02:00 → crosses midnight; duration 23:00→02:00 = 180 min.
        Assert.IsTrue(occ.CrossesMidnight);
        Assert.AreEqual(180, occ.DurationMinutes);
        Assert.AreEqual((2 * 60) + 0, occ.EndMinuteFromStartDay - (24 * 60));
    }

    // ---------- Recurring events ----------

    [TestMethod]
    public void Scenario_WeeklyMondayEvent_ExpandsToMondaysInRange()
    {
        var ev = JsonScenarioLoader.LoadEvent(WeeklyMondayJson);

        Assert.IsTrue(ev.IsRecurring());
        Assert.AreEqual(RecurrenceType.Weekly, ev.RecurringSchedule!.RecurrenceRule.RuleType);

        var results = _expander.Expand(ev,
            new LocalDateValue(2026, 4, 20),
            new LocalDateValue(2026, 5, 31),
            null);

        // Mondays in [4/20, 5/31]: 4/20, 4/27, 5/4, 5/11, 5/18, 5/25
        var expected = new[]
        {
            new LocalDateValue(2026, 4, 20),
            new LocalDateValue(2026, 4, 27),
            new LocalDateValue(2026, 5, 4),
            new LocalDateValue(2026, 5, 11),
            new LocalDateValue(2026, 5, 18),
            new LocalDateValue(2026, 5, 25),
        };
        CollectionAssert.AreEqual(expected, results.Select(r => r.Date).ToArray());
        Assert.IsTrue(results.All(r => r.StartTime.Equals(new LocalTimeValue(10, 0, 0))));
        // End 11:00 = start (10:00) + 60 minutes.
        Assert.IsTrue(results.All(r => r.DurationMinutes == 60));
        Assert.IsTrue(results.All(r => (r.StartMinuteOfDay + r.DurationMinutes) % (24 * 60) == 11 * 60));
        Assert.IsTrue(results.All(r => !r.IsMoved && !r.IsOverridden));
    }

    [TestMethod]
    public void Scenario_RecurringAllDayEvent_ExpandsToFridays()
    {
        var ev = JsonScenarioLoader.LoadEvent(RecurringAllDayJson);

        // All-day recurring in the duration model: start 00:00 + 1440 minutes.
        var recurring = ev.RecurringSchedule!;
        Assert.IsTrue(IsAllDay(recurring.AnchorUtc, recurring.DurationMinutes, ev.TimeZoneId));
        Assert.IsTrue(ev.IsRecurring());

        var results = _expander.Expand(ev,
            new LocalDateValue(2026, 4, 20),
            new LocalDateValue(2026, 5, 31),
            null);

        // Fridays in [4/20, 5/31]: 4/24, 5/1, 5/8, 5/15, 5/22, 5/29
        var expected = new[]
        {
            new LocalDateValue(2026, 4, 24),
            new LocalDateValue(2026, 5, 1),
            new LocalDateValue(2026, 5, 8),
            new LocalDateValue(2026, 5, 15),
            new LocalDateValue(2026, 5, 22),
            new LocalDateValue(2026, 5, 29),
        };
        CollectionAssert.AreEqual(expected, results.Select(r => r.Date).ToArray());
        Assert.IsTrue(results.All(r => r.StartMinuteOfDay == 0 && r.DurationMinutes == 24 * 60));
    }

    [TestMethod]
    public void Scenario_YearlyApril20Event_ExpandsAcrossYears()
    {
        var ev = JsonScenarioLoader.LoadEvent(YearlyApril20Json);

        Assert.IsTrue(ev.IsRecurring());
        Assert.AreEqual(RecurrenceType.Yearly, ev.RecurringSchedule!.RecurrenceRule.RuleType);

        var results = _expander.Expand(ev,
            new LocalDateValue(2026, 1, 1),
            new LocalDateValue(2030, 12, 31),
            null);

        var expected = new[]
        {
            new LocalDateValue(2026, 4, 20),
            new LocalDateValue(2027, 4, 20),
            new LocalDateValue(2028, 4, 20),
            new LocalDateValue(2029, 4, 20),
            new LocalDateValue(2030, 4, 20),
        };
        CollectionAssert.AreEqual(expected, results.Select(r => r.Date).ToArray());
        Assert.IsTrue(results.All(r => r.StartTime.Equals(new LocalTimeValue(8, 0, 0))));
        // End 12:00 = start (8:00) + 240 minutes.
        Assert.IsTrue(results.All(r => r.DurationMinutes == 240));
        Assert.IsTrue(results.All(r => (r.StartMinuteOfDay + r.DurationMinutes) % (24 * 60) == 12 * 60));
    }

    [TestMethod]
    public void Scenario_MonthlySecondMonday_NoHolidayOverlap_ExpandsUnchanged()
    {
        var ev = JsonScenarioLoader.LoadEvent(MonthlySecondMondayJson);
        var calendar = JsonScenarioLoader.LoadCalendar(BusinessCalendarJson);

        Assert.IsTrue(ev.IsRecurring());
        var adjustment = ev.RecurringSchedule!.RecurrenceRule.Adjustment;
        Assert.IsNotNull(adjustment);
        Assert.AreEqual(AdjustmentCondition.Holiday, adjustment!.Condition);
        Assert.AreEqual(AdjustmentShiftUnit.BusinessDay, adjustment.ShiftUnit);
        Assert.AreEqual(-1, adjustment.ShiftAmount);
        Assert.AreEqual("jp_default", adjustment.CalendarId!.Value);
        Assert.AreEqual(AdjustmentDirection.Backward, adjustment.Direction);

        var results = _expander.Expand(ev,
            new LocalDateValue(2026, 4, 1),
            new LocalDateValue(2026, 12, 31),
            calendar);

        // 2nd Mondays Apr-Dec 2026 (none collide with jp_default holidays in that window).
        var expected = new[]
        {
            new LocalDateValue(2026, 4, 13),
            new LocalDateValue(2026, 5, 11),
            new LocalDateValue(2026, 6, 8),
            new LocalDateValue(2026, 7, 13),
            new LocalDateValue(2026, 8, 10),
            new LocalDateValue(2026, 9, 14),
            new LocalDateValue(2026, 10, 12),
            new LocalDateValue(2026, 11, 9),
            new LocalDateValue(2026, 12, 14),
        };
        CollectionAssert.AreEqual(expected, results.Select(r => r.Date).ToArray());
    }

    [TestMethod]
    public void Scenario_MonthlySecondMonday_HolidayShiftsToPreviousBusinessDay()
    {
        var ev = JsonScenarioLoader.LoadEvent(MonthlySecondMondayJson);
        var calendar = JsonScenarioLoader.LoadCalendar(BusinessCalendarJson);

        // Inject a 2nd-Monday holiday so the backward adjustment kicks in.
        // 2026-05-11 is a Monday; Sat 5/9, Sun 5/10 are non-business → expect 5/8 (Fri).
        calendar.AddHoliday(new Holiday(new LocalDateValue(2026, 5, 11), "テスト休日"));

        var results = _expander.Expand(ev,
            new LocalDateValue(2026, 5, 1),
            new LocalDateValue(2026, 5, 31),
            calendar);

        Assert.HasCount(1, results);
        Assert.AreEqual(new LocalDateValue(2026, 5, 8), results[0].Date);
    }

    // ---------- Per-occurrence customizations ----------

    [TestMethod]
    public void Scenario_SkipOnce_RemovesThatOccurrence()
    {
        var ev = JsonScenarioLoader.LoadEvent(SkipOnceJson);

        Assert.HasCount(1, ev.Exceptions);
        Assert.IsTrue(ev.HasExceptionFor(new OccurrenceLocalKey(
            new LocalDateValue(2026, 5, 11), new LocalTimeValue(16, 0, 0))));

        var results = _expander.Expand(ev,
            new LocalDateValue(2026, 4, 20),
            new LocalDateValue(2026, 5, 31),
            null);

        // Mondays 4/20, 4/27, 5/4, 5/18, 5/25 — 5/11 is skipped.
        var expected = new[]
        {
            new LocalDateValue(2026, 4, 20),
            new LocalDateValue(2026, 4, 27),
            new LocalDateValue(2026, 5, 4),
            new LocalDateValue(2026, 5, 18),
            new LocalDateValue(2026, 5, 25),
        };
        CollectionAssert.AreEqual(expected, results.Select(r => r.Date).ToArray());
        Assert.IsFalse(results.Any(r => r.Date.Equals(new LocalDateValue(2026, 5, 11))));
    }

    [TestMethod]
    public void Scenario_OverrideOnce_AppliesOverrideFields()
    {
        var ev = JsonScenarioLoader.LoadEvent(OverrideOnceJson);

        Assert.HasCount(1, ev.Exceptions);

        var results = _expander.Expand(ev,
            new LocalDateValue(2026, 4, 20),
            new LocalDateValue(2026, 5, 31),
            null);

        // Tuesdays in range: 4/21, 4/28, 5/5, 5/12, 5/19, 5/26 → 6 occurrences.
        Assert.HasCount(6, results);

        var overridden = results.Single(r => r.Date.Equals(new LocalDateValue(2026, 5, 12)));
        Assert.IsTrue(overridden.IsOverridden);
        Assert.AreEqual("開発定例（短縮）", overridden.Title.Value);
        Assert.AreEqual("会議室C", overridden.Location!.Value);
        Assert.AreEqual(DomainVisibility.Private, overridden.Visibility);
        Assert.AreEqual(new LocalTimeValue(10, 30, 0), overridden.StartTime);
        // End 11:00 = start (10:30) + 30 minutes.
        Assert.AreEqual(30, overridden.DurationMinutes);
        Assert.AreEqual(11 * 60, (overridden.StartMinuteOfDay + overridden.DurationMinutes) % (24 * 60));

        // Other occurrences keep base values.
        var untouched = results.First(r => r.Date.Equals(new LocalDateValue(2026, 4, 21)));
        Assert.IsFalse(untouched.IsOverridden);
        Assert.AreEqual("開発定例", untouched.Title.Value);
        Assert.AreEqual("会議室B", untouched.Location!.Value);
        Assert.AreEqual(DomainVisibility.Public, untouched.Visibility);
        Assert.AreEqual(new LocalTimeValue(10, 0, 0), untouched.StartTime);
        // End 11:00 = start (10:00) + 60 minutes.
        Assert.AreEqual(60, untouched.DurationMinutes);
        Assert.AreEqual(11 * 60, (untouched.StartMinuteOfDay + untouched.DurationMinutes) % (24 * 60));
    }

    [TestMethod]
    public void Scenario_MoveOnce_ShowsMovedOccurrenceOnly()
    {
        var ev = JsonScenarioLoader.LoadEvent(MoveOnceJson);

        Assert.HasCount(1, ev.Moves);

        var results = _expander.Expand(ev,
            new LocalDateValue(2026, 6, 1),
            new LocalDateValue(2026, 6, 30),
            null);

        // Original 1st-Monday-of-June (6/1) must not appear; moved 6/2 should.
        Assert.HasCount(1, results);
        var moved = results[0];
        Assert.IsTrue(moved.IsMoved);
        Assert.AreEqual(new LocalDateValue(2026, 6, 2), moved.Date);
        Assert.AreEqual(new LocalTimeValue(15, 0, 0), moved.StartTime);
        // End 16:00 = start (15:00) + 60 minutes.
        Assert.AreEqual(60, moved.DurationMinutes);
        Assert.AreEqual(16 * 60, (moved.StartMinuteOfDay + moved.DurationMinutes) % (24 * 60));
        Assert.AreEqual("月次報告会（振替）", moved.Title.Value);
        Assert.AreEqual("会議室E", moved.Location!.Value);
    }

    [TestMethod]
    public void Scenario_MoveOnce_FullRange_ReplacesOriginalWithMoved()
    {
        var ev = JsonScenarioLoader.LoadEvent(MoveOnceJson);

        var results = _expander.Expand(ev,
            new LocalDateValue(2026, 4, 1),
            new LocalDateValue(2026, 12, 31),
            null);

        // 1st Mondays Apr-Dec 2026: 4/6, 5/4, 6/1→moved to 6/2, 7/6, 8/3, 9/7, 10/5, 11/2, 12/7
        var expected = new[]
        {
            new LocalDateValue(2026, 4, 6),
            new LocalDateValue(2026, 5, 4),
            new LocalDateValue(2026, 6, 2),
            new LocalDateValue(2026, 7, 6),
            new LocalDateValue(2026, 8, 3),
            new LocalDateValue(2026, 9, 7),
            new LocalDateValue(2026, 10, 5),
            new LocalDateValue(2026, 11, 2),
            new LocalDateValue(2026, 12, 7),
        };
        CollectionAssert.AreEqual(expected, results.Select(r => r.Date).ToArray());
        Assert.IsFalse(results.Any(r => r.Date.Equals(new LocalDateValue(2026, 6, 1))));
        Assert.HasCount(1, results.Where(r => r.IsMoved).ToList());
    }

    // ---------- Business calendar ----------

    [TestMethod]
    public void Scenario_BusinessCalendar_LoadsHolidaysAndWorkdays()
    {
        var calendar = JsonScenarioLoader.LoadCalendar(BusinessCalendarJson);

        Assert.AreEqual("jp_default", calendar.Id.Value);
        Assert.AreEqual("Japan Default Business Calendar", calendar.Name);

        var workdays = calendar.Workdays.ToHashSet();
        Assert.HasCount(5, workdays);
        Assert.IsTrue(workdays.SetEquals(new[]
        {
            Weekday.Monday, Weekday.Tuesday, Weekday.Wednesday, Weekday.Thursday, Weekday.Friday,
        }));

        Assert.IsTrue(calendar.IsHoliday(new LocalDateValue(2026, 1, 1)));
        Assert.IsTrue(calendar.IsHoliday(new LocalDateValue(2026, 1, 12)));
        Assert.IsTrue(calendar.IsHoliday(new LocalDateValue(2026, 2, 11)));
        Assert.IsTrue(calendar.IsHoliday(new LocalDateValue(2026, 4, 29)));
        Assert.IsFalse(calendar.IsHoliday(new LocalDateValue(2026, 4, 28)));

        // Saturday 4/18 is non-workday; Monday 4/20 is workday.
        Assert.IsFalse(calendar.IsBusinessDay(new LocalDateValue(2026, 4, 18)));
        Assert.IsTrue(calendar.IsBusinessDay(new LocalDateValue(2026, 4, 20)));
    }

    [TestMethod]
    public void Scenario_AllJsonInputs_LoadWithoutError()
    {
        var eventFiles = new[]
        {
            SingleNormalJson, SingleAllDayJson, SingleCrossDayJson,
            WeeklyMondayJson, RecurringAllDayJson, YearlyApril20Json,
            MonthlySecondMondayJson, SkipOnceJson, OverrideOnceJson, MoveOnceJson,
        };

        foreach (var file in eventFiles)
        {
            var ev = JsonScenarioLoader.LoadEvent(file);
            Assert.IsNotNull(ev, $"{file} failed to load.");
            Assert.IsNotNull(ev.Id);
            Assert.IsNotNull(ev.Title);
        }

        var calendar = JsonScenarioLoader.LoadCalendar(BusinessCalendarJson);
        Assert.IsNotNull(calendar);
    }
}
