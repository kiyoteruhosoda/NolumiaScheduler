using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.ValueObjects;

namespace NolumiaSchedulerTest;

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
