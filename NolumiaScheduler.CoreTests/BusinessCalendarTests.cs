using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.ValueObjects;

namespace NolumiaScheduler.CoreTests;

[TestClass]
public class BusinessCalendarTests
{
    private static readonly TimeZoneId Tokyo = new("Asia/Tokyo");

    [TestMethod]
    public void IsBusinessDay_Weekday_NoHoliday_ReturnsTrue()
    {
        var cal = CreateDefaultCalendar();
        // 2026-04-20 is Monday
        Assert.IsTrue(cal.IsBusinessDay(new LocalDateValue(2026, 4, 20)));
    }

    [TestMethod]
    public void IsBusinessDay_Weekend_ReturnsFalse()
    {
        var cal = CreateDefaultCalendar();
        // 2026-04-18 is Saturday
        Assert.IsFalse(cal.IsBusinessDay(new LocalDateValue(2026, 4, 18)));
    }

    [TestMethod]
    public void IsBusinessDay_Holiday_ReturnsFalse()
    {
        var cal = CreateDefaultCalendar();
        // 2026-01-01 is Thursday but holiday
        Assert.IsFalse(cal.IsBusinessDay(new LocalDateValue(2026, 1, 1)));
    }

    [TestMethod]
    public void ShiftBusinessDays_Forward_SkipsWeekend()
    {
        var cal = CreateDefaultCalendar();
        // 2026-04-17 is Friday, shift +1 should be Monday 2026-04-20
        var result = cal.ShiftBusinessDays(new LocalDateValue(2026, 4, 17), 1);
        Assert.AreEqual(new LocalDateValue(2026, 4, 20), result);
    }

    [TestMethod]
    public void ShiftBusinessDays_Backward()
    {
        var cal = CreateDefaultCalendar();
        // 2026-04-20 is Monday, shift -1 should be Friday 2026-04-17
        var result = cal.ShiftBusinessDays(new LocalDateValue(2026, 4, 20), -1);
        Assert.AreEqual(new LocalDateValue(2026, 4, 17), result);
    }

    [TestMethod]
    public void ShiftBusinessDays_ShiftOnHolidaysOnly_DoesNotSkipWeekend()
    {
        // ShiftOnHolidaysOnly=true: weekends are valid landing days, only holidays are skipped.
        var cal = CreateCalendarWithShiftOnHolidaysOnly();
        // 2026-04-17 is Friday, shift +1 should be Saturday 2026-04-18 (NOT Monday, because weekends are allowed)
        var result = cal.ShiftBusinessDays(new LocalDateValue(2026, 4, 17), 1);
        Assert.AreEqual(new LocalDateValue(2026, 4, 18), result);
    }

    [TestMethod]
    public void ShiftBusinessDays_ShiftOnHolidaysOnly_SkipsHoliday()
    {
        // ShiftOnHolidaysOnly=true: holidays are still skipped.
        // 2025-12-31 is Wednesday; add +1 → 2026-01-01 is a holiday → skip → 2026-01-02 (Friday)
        var cal = CreateCalendarWithShiftOnHolidaysOnly();
        var result = cal.ShiftBusinessDays(new LocalDateValue(2025, 12, 31), 1);
        Assert.AreEqual(new LocalDateValue(2026, 1, 2), result);
    }

    [TestMethod]
    public void ShiftOnHolidaysOnly_DefaultIsFalse()
    {
        var cal = CreateDefaultCalendar();
        Assert.IsFalse(cal.ShiftOnHolidaysOnly);
    }

    [TestMethod]
    public void Update_SetsShiftOnHolidaysOnly()
    {
        var cal = CreateDefaultCalendar();
        cal.Update(cal.Name, cal.Workdays, shiftOnHolidaysOnly: true);
        Assert.IsTrue(cal.ShiftOnHolidaysOnly);
    }

    private static BusinessCalendar CreateCalendarWithShiftOnHolidaysOnly()
    {
        return new BusinessCalendar(
            new BusinessCalendarId("jp_shift_holidays"),
            "Japan Shift Holidays Only",
            Tokyo,
            [Weekday.Monday, Weekday.Tuesday, Weekday.Wednesday, Weekday.Thursday, Weekday.Friday],
            [new Holiday(new LocalDateValue(2026, 1, 1), "元日")],
            shiftOnHolidaysOnly: true);
    }

    private static BusinessCalendar CreateDefaultCalendar()
    {
        return new BusinessCalendar(
            new BusinessCalendarId("jp_default"),
            "Japan Default",
            Tokyo,
            [Weekday.Monday, Weekday.Tuesday, Weekday.Wednesday, Weekday.Thursday, Weekday.Friday],
            [new Holiday(new LocalDateValue(2026, 1, 1), "元日")]);
    }
}
