using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.Entities;
using NolumiaScheduler.Domain.Exceptions;
using NolumiaScheduler.Domain.ValueObjects;
using Visibility = NolumiaScheduler.Domain.ValueObjects.Visibility;
using Location = NolumiaScheduler.Domain.ValueObjects.Location;

namespace NolumiaScheduler.CoreTests;

[TestClass]
public class CalendarEventTests
{
    private static readonly TimeZoneId Tokyo = new("Asia/Tokyo");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static DateTimeOffset Utc(int y, int mo, int d, int h, int mi) =>
        LocalSchedulePoint.StartInstant(
            new LocalDateValue(y, mo, d), new LocalTimeValue(h, mi, 0), Tokyo.ToTimeZoneInfo())
            .ToUniversalTime();

    [TestMethod]
    public void CreateSingleEvent_ShouldSetProperties()
    {
        var ev = CalendarEvent.CreateSingle(
            new EventId("evt_001"),
            new EventTitle("チE��ト会議"),
            new Location("会議室A"),
            Visibility.Public,
            null, null, Tokyo,
            new SingleEventSchedule(Utc(2026, 4, 20, 10, 0), 60),
            Now);

        Assert.IsTrue(ev.IsSingle());
        Assert.IsFalse(ev.IsRecurring());
        Assert.AreEqual("チE��ト会議", ev.Title.Value);
        Assert.AreEqual("会議室A", ev.Location!.Value);
        Assert.AreEqual(Visibility.Public, ev.Visibility);
    }

    [TestMethod]
    public void CreateSingleEvent_NonPositiveDuration_ShouldThrow()
    {
        Assert.ThrowsExactly<DomainException>(() =>
            new SingleEventSchedule(Utc(2026, 4, 20, 12, 0), 0));
    }

    [TestMethod]
    public void CreateRecurringEvent_Weekly_ShouldSetProperties()
    {
        var rule = new RecurrenceRule(
            RecurrenceType.Weekly, 1,
            new LocalDateValue(2026, 12, 31),
            weekly: new WeeklyRule([Weekday.Monday]));

        var schedule = new RecurringEventSchedule(
            Utc(2026, 4, 20, 10, 0),
            60,
            rule);

        var ev = CalendarEvent.CreateRecurring(
            new EventId("evt_002"),
            new EventTitle("定例会議"),
            null, Visibility.Public, null, null, Tokyo,
            schedule, Now);

        Assert.IsTrue(ev.IsRecurring());
        Assert.AreEqual(RecurrenceType.Weekly, ev.RecurringSchedule!.RecurrenceRule.RuleType);
    }

    [TestMethod]
    public void RecurringEvent_CrossDay_IsAllowed()
    {
        // Crossing midnight is now allowed: a 23:00 start lasting 2h ends at 01:00 the next day,
        // expressed purely through the duration (docs/time-model.md).
        var rule = new RecurrenceRule(
            RecurrenceType.Weekly, 1,
            new LocalDateValue(2026, 12, 31),
            weekly: new WeeklyRule([Weekday.Monday]));

        var schedule = new RecurringEventSchedule(
            Utc(2026, 4, 20, 23, 0), 120, rule);

        Assert.AreEqual(120, schedule.DurationMinutes);
    }

    [TestMethod]
    public void SkipOccurrence_ShouldAddException()
    {
        var ev = CreateWeeklyRecurring();
        var key = new OccurrenceLocalKey(new LocalDateValue(2026, 4, 27), new LocalTimeValue(10, 0, 0));

        ev.SkipOccurrence(key, Now);

        Assert.IsTrue(ev.HasExceptionFor(key));
        Assert.HasCount(1, ev.Exceptions);
        Assert.AreEqual(ExceptionType.Skip, ev.Exceptions[0].Type);
    }

    [TestMethod]
    public void SkipOccurrence_CanBeCalledAgainToReplaceExistingSkip()
    {
        // Skipping an already-skipped occurrence is idempotent: the result is still skipped.
        var ev = CreateWeeklyRecurring();
        var key = new OccurrenceLocalKey(new LocalDateValue(2026, 4, 27), new LocalTimeValue(10, 0, 0));
        ev.SkipOccurrence(key, Now);

        ev.SkipOccurrence(key, Now);

        Assert.IsTrue(ev.HasExceptionFor(key));
        Assert.HasCount(1, ev.Exceptions);
        Assert.AreEqual(ExceptionType.Skip, ev.Exceptions[0].Type);
    }

    [TestMethod]
    public void ChangeRecurrenceEndDate_ShouldUpdate()
    {
        var ev = CreateWeeklyRecurring();
        var newEnd = new LocalDateValue(2026, 6, 30);

        ev.ChangeRecurrenceEndDate(newEnd, Now);

        Assert.AreEqual(newEnd, ev.RecurringSchedule!.RecurrenceRule.EndDate);
    }

    [TestMethod]
    public void ChangeDetails_ShouldUpdateTitleAndVersion()
    {
        var ev = CreateWeeklyRecurring();
        var originalVersion = ev.Version.Value;

        ev.ChangeDetails(
            new EventTitle("新タイトル"), null, Visibility.Private, null, null, Now);

        Assert.AreEqual("新タイトル", ev.Title.Value);
        Assert.AreEqual(Visibility.Private, ev.Visibility);
        Assert.AreEqual(originalVersion + 1, ev.Version.Value);
    }

    [TestMethod]
    public void SingleEvent_SkipOccurrence_ShouldThrow()
    {
        var ev = CalendarEvent.CreateSingle(
            new EventId("evt_s01"),
            new EventTitle("単発"),
            null, Visibility.Public, null, null, Tokyo,
            new SingleEventSchedule(Utc(2026, 4, 20, 10, 0), 60),
            Now);

        var key = new OccurrenceLocalKey(new LocalDateValue(2026, 4, 20), new LocalTimeValue(10, 0, 0));
        Assert.ThrowsExactly<DomainException>(() => ev.SkipOccurrence(key, Now));
    }

    [TestMethod]
    public void RemoveOccurrenceException_ShouldRemove()
    {
        var ev = CreateWeeklyRecurring();
        var key = new OccurrenceLocalKey(new LocalDateValue(2026, 4, 27), new LocalTimeValue(10, 0, 0));
        ev.SkipOccurrence(key, Now);

        ev.RemoveOccurrenceException(key, Now);

        Assert.IsFalse(ev.HasExceptionFor(key));
    }

    [TestMethod]
    public void RemoveOccurrenceException_NotFound_ShouldThrow()
    {
        var ev = CreateWeeklyRecurring();
        var key = new OccurrenceLocalKey(new LocalDateValue(2026, 4, 27), new LocalTimeValue(10, 0, 0));

        Assert.ThrowsExactly<DomainException>(() => ev.RemoveOccurrenceException(key, Now));
    }

    [TestMethod]
    public void ChangeDetails_IncreasesVersionByOne()
    {
        var ev = CreateWeeklyRecurring();
        var v0 = ev.Version.Value;

        ev.ChangeDetails(new EventTitle("更新1"), null, Visibility.Public, null, null, Now);
        ev.ChangeDetails(new EventTitle("更新2"), null, Visibility.Private, null, null, Now);

        Assert.AreEqual(v0 + 2, ev.Version.Value);
    }

    private static CalendarEvent CreateWeeklyRecurring()
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
            new EventId("evt_r01"),
            new EventTitle("定例会議"),
            new Location("会議室A"),
            Visibility.Public, null, null, Tokyo,
            schedule, Now);
    }
}
