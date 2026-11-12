using NolumiaScheduler.Domain.ValueObjects;
using NolumiaScheduler.Infrastructure.Server;

namespace NolumiaScheduler.CoreTests.Server;

[TestClass]
public class ServerDaysOffCalendarTests
{
    private static CalendarDto Workweek(params string[] days)
        => new(1, "WORKWEEK", "Workweek", "DEFAULT", 0, false, days, null, false, "WORK");

    [TestMethod]
    public void OnlyDaysInCountedLayersAreHolidays_WeekdayOffAndUncountedLayersAreNot()
    {
        var marks = new[]
        {
            new DayOffMarkDto("2026-11-03", "NATIONAL_HOLIDAY", 2, "文化の日", true),
            new DayOffMarkDto("2026-11-07", ServerDaysOffCalendar.WeeklyReason, 1, null, true),
            new DayOffMarkDto("2026-11-10", "PERSONAL", 3, null, false),
        };

        var calendar = ServerDaysOffCalendar.Build([Workweek("MO", "TU", "WE", "TH", "FR")], marks, "Asia/Tokyo");

        Assert.IsTrue(calendar.IsHoliday(new LocalDateValue(2026, 11, 3)));
        Assert.IsFalse(calendar.IsHoliday(new LocalDateValue(2026, 11, 7)), "Saturday is a weekday off (not a holiday)");
        Assert.IsFalse(calendar.IsHoliday(new LocalDateValue(2026, 11, 10)), "A day in an uncounted layer");
        Assert.IsFalse(calendar.IsBusinessDay(new LocalDateValue(2026, 11, 7)));
        Assert.AreEqual("文化の日", calendar.Holidays.Single().Name);
    }

    [TestMethod]
    public void CountingBusinessDays_SkipsHolidaysAndNonWorkingWeekdays()
    {
        var marks = new[] { new DayOffMarkDto("2026-11-03", "NATIONAL_HOLIDAY", 2, null, true) };
        var calendar = ServerDaysOffCalendar.Build([Workweek("MO", "TU", "WE", "TH", "FR")], marks, "Asia/Tokyo");

        // Fri 10/30 + 2 business days: Mon 11/2, (Tue 11/3 holiday) → Wed 11/4.
        Assert.AreEqual(new LocalDateValue(2026, 11, 4), calendar.ShiftBusinessDays(new LocalDateValue(2026, 10, 30), 2));
    }

    [TestMethod]
    public void NoBusinessDayLayer_DefaultsToMondayThroughFriday()
    {
        var calendar = ServerDaysOffCalendar.Build([], [], "Asia/Tokyo");

        Assert.IsTrue(calendar.IsBusinessDay(new LocalDateValue(2026, 11, 2)));
        Assert.IsFalse(calendar.IsBusinessDay(new LocalDateValue(2026, 11, 1)));
    }

    [TestMethod]
    public void UsesWeekdaysOfBusinessDayLayer()
    {
        var calendar = ServerDaysOffCalendar.Build([Workweek("SA", "SU")], [], "Asia/Tokyo");

        Assert.IsTrue(calendar.IsBusinessDay(new LocalDateValue(2026, 11, 1)));
        Assert.IsFalse(calendar.IsBusinessDay(new LocalDateValue(2026, 11, 2)));
    }
}
