using NolumiaScheduler.Domain.ValueObjects;
using Visibility = NolumiaScheduler.Domain.ValueObjects.Visibility;
using Location = NolumiaScheduler.Domain.ValueObjects.Location;

namespace NolumiaSchedulerTest;

[TestClass]
public class ValueObjectTests
{
    [TestMethod]
    public void LocalDateValue_Equality()
    {
        var d1 = new LocalDateValue(2026, 4, 20);
        var d2 = new LocalDateValue(2026, 4, 20);
        Assert.AreEqual(d1, d2);
        Assert.IsTrue(d1 == d2);
    }

    [TestMethod]
    public void LocalDateValue_Comparison()
    {
        var d1 = new LocalDateValue(2026, 4, 19);
        var d2 = new LocalDateValue(2026, 4, 20);
        Assert.IsTrue(d1 < d2);
        Assert.IsTrue(d2 > d1);
    }

    [TestMethod]
    public void LocalDateValue_AddDays()
    {
        var d = new LocalDateValue(2026, 4, 30);
        var result = d.AddDays(1);
        Assert.AreEqual(new LocalDateValue(2026, 5, 1), result);
    }

    [TestMethod]
    public void LocalTimeValue_Comparison()
    {
        var t1 = new LocalTimeValue(9, 0, 0);
        var t2 = new LocalTimeValue(10, 0, 0);
        Assert.IsLessThan(0, t1.CompareTo(t2));
    }

    [TestMethod]
    public void OccurrenceLocalKey_Equality()
    {
        var k1 = new OccurrenceLocalKey(new LocalDateValue(2026, 4, 20), new LocalTimeValue(10, 0, 0));
        var k2 = new OccurrenceLocalKey(new LocalDateValue(2026, 4, 20), new LocalTimeValue(10, 0, 0));
        Assert.AreEqual(k1, k2);
    }

    [TestMethod]
    public void RecurrenceRule_WeeklyRequiresWeeklyRule()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            new RecurrenceRule(RecurrenceType.Weekly, 1, new LocalDateValue(2026, 12, 31)));
    }

    [TestMethod]
    public void RecurrenceRule_IntervalMustBePositive()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new RecurrenceRule(RecurrenceType.Weekly, 0, new LocalDateValue(2026, 12, 31),
                weekly: new WeeklyRule([Weekday.Monday])));
    }

    [TestMethod]
    public void EventTitle_CreatesSuccessfully()
    {
        var title = new EventTitle("テスト");
        Assert.AreEqual("テスト", title.Value);
    }

    [TestMethod]
    public void TimeZoneId_Invalid_ShouldThrow()
    {
        Assert.ThrowsExactly<TimeZoneNotFoundException>(() => new TimeZoneId("Invalid/Zone"));
    }

    [TestMethod]
    public void VersionNo_Next_Increments()
    {
        var v = VersionNo.Initial();
        Assert.AreEqual(1, v.Value);
        Assert.AreEqual(2, v.Next().Value);
    }

    [TestMethod]
    public void LocalDateValue_AddMonths()
    {
        var d = new LocalDateValue(2026, 1, 31);
        // January + 1 month = February, but Feb has no 31st → rolls to Feb 28
        var result = d.AddMonths(1);
        Assert.AreEqual(new LocalDateValue(2026, 2, 28), result);
    }

    [TestMethod]
    public void LocalDateValue_AddYears()
    {
        var d = new LocalDateValue(2026, 4, 20);
        Assert.AreEqual(new LocalDateValue(2028, 4, 20), d.AddYears(2));
    }

    [TestMethod]
    public void LocalTimeValue_Equality()
    {
        var t1 = new LocalTimeValue(10, 30, 0);
        var t2 = new LocalTimeValue(10, 30, 0);
        Assert.AreEqual(t1, t2);
        Assert.IsTrue(t1.Equals(t2));
    }

    [TestMethod]
    public void RecurrenceRule_MonthlyRequiresMonthlyRule()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            new RecurrenceRule(RecurrenceType.Monthly, 1, new LocalDateValue(2026, 12, 31)));
    }

    [TestMethod]
    public void RecurrenceRule_YearlyRequiresYearlyRule()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            new RecurrenceRule(RecurrenceType.Yearly, 1, new LocalDateValue(2026, 12, 31)));
    }

    [TestMethod]
    public void OccurrenceLocalKey_DifferentDate_NotEqual()
    {
        var k1 = new OccurrenceLocalKey(new LocalDateValue(2026, 4, 20), new LocalTimeValue(10, 0, 0));
        var k2 = new OccurrenceLocalKey(new LocalDateValue(2026, 4, 21), new LocalTimeValue(10, 0, 0));
        Assert.AreNotEqual(k1, k2);
    }

    [TestMethod]
    public void LocalDateValue_DayOfWeek_CorrectForKnownDate()
    {
        // 2026-04-20 is Monday
        var d = new LocalDateValue(2026, 4, 20);
        Assert.AreEqual(DayOfWeek.Monday, d.DayOfWeek);
    }

    [TestMethod]
    public void LocalDateValue_InvalidDate_ShouldThrow()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new LocalDateValue(2026, 2, 30));
    }
}
