using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.Entities;
using NolumiaScheduler.Domain.Exceptions;
using NolumiaScheduler.Domain.Services;
using NolumiaScheduler.Domain.ValueObjects;
using Visibility = NolumiaScheduler.Domain.ValueObjects.Visibility;
using Location = NolumiaScheduler.Domain.ValueObjects.Location;

namespace NolumiaSchedulerTest;

[TestClass]
public class CalendarEventTests
{
    private static readonly TimeZoneId Tokyo = new("Asia/Tokyo");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [TestMethod]
    public void CreateSingleEvent_ShouldSetProperties()
    {
        var ev = CalendarEvent.CreateSingle(
            new EventId("evt_001"),
            new EventTitle("チE��ト会議"),
            new Location("会議室A"),
            Visibility.Public,
            null, null, Tokyo, false,
            new SingleEventSchedule(
                new DateTimeOffset(2026, 4, 20, 10, 0, 0, TimeSpan.FromHours(9)),
                new DateTimeOffset(2026, 4, 20, 11, 0, 0, TimeSpan.FromHours(9))),
            Now);

        Assert.IsTrue(ev.IsSingle());
        Assert.IsFalse(ev.IsRecurring());
        Assert.AreEqual("チE��ト会議", ev.Title.Value);
        Assert.AreEqual("会議室A", ev.Location!.Value);
        Assert.AreEqual(Visibility.Public, ev.Visibility);
    }

    [TestMethod]
    public void CreateSingleEvent_StartAfterEnd_ShouldThrow()
    {
        Assert.ThrowsExactly<DomainException>(() =>
            new SingleEventSchedule(
                new DateTimeOffset(2026, 4, 20, 12, 0, 0, TimeSpan.FromHours(9)),
                new DateTimeOffset(2026, 4, 20, 10, 0, 0, TimeSpan.FromHours(9))));
    }

    [TestMethod]
    public void CreateRecurringEvent_Weekly_ShouldSetProperties()
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

        var ev = CalendarEvent.CreateRecurring(
            new EventId("evt_002"),
            new EventTitle("定例会議"),
            null, Visibility.Public, null, null, Tokyo, false,
            schedule, Now);

        Assert.IsTrue(ev.IsRecurring());
        Assert.AreEqual(RecurrenceType.Weekly, ev.RecurringSchedule!.RecurrenceRule.RuleType);
    }

    [TestMethod]
    public void RecurringEvent_CrossDay_ShouldThrow()
    {
        var startDate = new LocalDateValue(2026, 4, 20);
        var rule = new RecurrenceRule(
            RecurrenceType.Weekly, 1,
            new LocalDateValue(2026, 12, 31),
            weekly: new WeeklyRule([Weekday.Monday]));

        Assert.ThrowsExactly<DomainException>(() =>
            new RecurringEventSchedule(
                startDate,
                new LocalTimeValue(23, 0, 0),
                new LocalTimeValue(1, 0, 0),
                rule, false));
    }

    [TestMethod]
    public void SkipOccurrence_ShouldAddException()
    {
        var ev = CreateWeeklyRecurring();
        var key = new OccurrenceLocalKey(new LocalDateValue(2026, 4, 27), new LocalTimeValue(10, 0, 0));

        ev.SkipOccurrence(key, Now);

        Assert.IsTrue(ev.HasExceptionFor(key));
        Assert.AreEqual(1, ev.Exceptions.Count);
        Assert.AreEqual(ExceptionType.Skip, ev.Exceptions[0].Type);
    }

    [TestMethod]
    public void OverrideOccurrence_ShouldAddException()
    {
        var ev = CreateWeeklyRecurring();
        var key = new OccurrenceLocalKey(new LocalDateValue(2026, 4, 27), new LocalTimeValue(10, 0, 0));
        var ov = new ExceptionOverride(title: new EventTitle("短縮会議"));

        ev.OverrideOccurrence(key, ov, Now);

        Assert.IsTrue(ev.HasExceptionFor(key));
        Assert.AreEqual(ExceptionType.Override, ev.Exceptions[0].Type);
        Assert.AreEqual("短縮会議", ev.Exceptions[0].Override!.Title!.Value);
    }

    [TestMethod]
    public void MoveOccurrence_ShouldAddMove()
    {
        var ev = CreateWeeklyRecurring();
        var key = new OccurrenceLocalKey(new LocalDateValue(2026, 4, 27), new LocalTimeValue(10, 0, 0));
        var move = new EventMove(key, new LocalDateValue(2026, 4, 28),
            new LocalTimeValue(14, 0, 0), new LocalTimeValue(15, 0, 0));

        ev.MoveOccurrence(move, Now);

        Assert.IsTrue(ev.HasMoveFor(key));
        Assert.AreEqual(1, ev.Moves.Count);
    }

    [TestMethod]
    public void MoveOccurrence_AlreadyExcepted_ShouldThrow()
    {
        var ev = CreateWeeklyRecurring();
        var key = new OccurrenceLocalKey(new LocalDateValue(2026, 4, 27), new LocalTimeValue(10, 0, 0));
        ev.SkipOccurrence(key, Now);

        var move = new EventMove(key, new LocalDateValue(2026, 4, 28),
            new LocalTimeValue(14, 0, 0), new LocalTimeValue(15, 0, 0));

        Assert.ThrowsExactly<DomainException>(() => ev.MoveOccurrence(move, Now));
    }

    [TestMethod]
    public void SkipOccurrence_AlreadyMoved_ShouldThrow()
    {
        var ev = CreateWeeklyRecurring();
        var key = new OccurrenceLocalKey(new LocalDateValue(2026, 4, 27), new LocalTimeValue(10, 0, 0));
        var move = new EventMove(key, new LocalDateValue(2026, 4, 28),
            new LocalTimeValue(14, 0, 0), new LocalTimeValue(15, 0, 0));
        ev.MoveOccurrence(move, Now);

        Assert.ThrowsExactly<DomainException>(() => ev.SkipOccurrence(key, Now));
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
            null, Visibility.Public, null, null, Tokyo, false,
            new SingleEventSchedule(
                new DateTimeOffset(2026, 4, 20, 10, 0, 0, TimeSpan.FromHours(9)),
                new DateTimeOffset(2026, 4, 20, 11, 0, 0, TimeSpan.FromHours(9))),
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

    private static CalendarEvent CreateWeeklyRecurring()
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
            new EventId("evt_r01"),
            new EventTitle("定例会議"),
            new Location("会議室A"),
            Visibility.Public, null, null, Tokyo, false,
            schedule, Now);
    }
}
