using NolumiaScheduler.Domain.ValueObjects;
using NolumiaScheduler.Infrastructure.Server;

namespace NolumiaScheduler.CoreTests.Server;

[TestClass]
public class ServerDaysOffCalendarTests
{
    private static CalendarDto Workweek(params string[] days)
        => new(1, "WORKWEEK", "Workweek", "DEFAULT", 0, false, days, null, false, "WORK");

    [TestMethod]
    public void 休みとして数える層の日だけが休日_曜日の休みと数えない層は休日ではない()
    {
        var marks = new[]
        {
            new DayOffMarkDto("2026-11-03", "NATIONAL_HOLIDAY", 2, "文化の日", true),
            new DayOffMarkDto("2026-11-07", ServerDaysOffCalendar.WeeklyReason, 1, null, true),
            new DayOffMarkDto("2026-11-10", "PERSONAL", 3, null, false),
        };

        var calendar = ServerDaysOffCalendar.Build([Workweek("MO", "TU", "WE", "TH", "FR")], marks, "Asia/Tokyo");

        Assert.IsTrue(calendar.IsHoliday(new LocalDateValue(2026, 11, 3)));
        Assert.IsFalse(calendar.IsHoliday(new LocalDateValue(2026, 11, 7)), "土曜は曜日の休み（休日ではない）");
        Assert.IsFalse(calendar.IsHoliday(new LocalDateValue(2026, 11, 10)), "数えない層の日");
        Assert.IsFalse(calendar.IsBusinessDay(new LocalDateValue(2026, 11, 7)));
        Assert.AreEqual("文化の日", calendar.Holidays.Single().Name);
    }

    [TestMethod]
    public void 営業日を数えるときは祝日と稼働しない曜日を飛ばす()
    {
        var marks = new[] { new DayOffMarkDto("2026-11-03", "NATIONAL_HOLIDAY", 2, null, true) };
        var calendar = ServerDaysOffCalendar.Build([Workweek("MO", "TU", "WE", "TH", "FR")], marks, "Asia/Tokyo");

        // Fri 10/30 + 2 business days: Mon 11/2, (Tue 11/3 holiday) → Wed 11/4.
        Assert.AreEqual(new LocalDateValue(2026, 11, 4), calendar.ShiftBusinessDays(new LocalDateValue(2026, 10, 30), 2));
    }

    [TestMethod]
    public void 営業日の層が無ければ月曜から金曜()
    {
        var calendar = ServerDaysOffCalendar.Build([], [], "Asia/Tokyo");

        Assert.IsTrue(calendar.IsBusinessDay(new LocalDateValue(2026, 11, 2)));
        Assert.IsFalse(calendar.IsBusinessDay(new LocalDateValue(2026, 11, 1)));
    }

    [TestMethod]
    public void 営業日の層の曜日を使う()
    {
        var calendar = ServerDaysOffCalendar.Build([Workweek("SA", "SU")], [], "Asia/Tokyo");

        Assert.IsTrue(calendar.IsBusinessDay(new LocalDateValue(2026, 11, 1)));
        Assert.IsFalse(calendar.IsBusinessDay(new LocalDateValue(2026, 11, 2)));
    }
}
