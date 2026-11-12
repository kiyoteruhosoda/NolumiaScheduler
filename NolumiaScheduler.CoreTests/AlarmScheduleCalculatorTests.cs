using NolumiaScheduler.Application.Services;
using NolumiaScheduler.Domain.ValueObjects;

namespace NolumiaScheduler.CoreTests;

[TestClass]
public class AlarmScheduleCalculatorTests
{
    [TestMethod]
    public void EventAlarm_Default_AlsoEnablesStartTimeNotification()
    {
        var alarm = EventAlarm.Default;
        Assert.IsTrue(alarm.NotifyAtStart);
        Assert.IsTrue(alarm.IsEnabled);
        Assert.IsTrue(alarm.Notify15Min);
        Assert.IsTrue(alarm.Notify5Min);
        Assert.IsTrue(alarm.Notify1Min);
    }

    [TestMethod]
    public void Offsets_AreInOrder15_5_1_0()
    {
        CollectionAssert.AreEqual(new[] { 15, 5, 1, 0 }, Rec0());

        static int[] Rec0() => [.. AlarmScheduleCalculator.Offsets];
    }

    [TestMethod]
    public void IsOffsetEnabled_MatchesEachFlag()
    {
        var alarm = new EventAlarm(true, Notify15Min: true, Notify5Min: false, Notify1Min: true, NotifyAtStart: false);

        Assert.IsTrue(AlarmScheduleCalculator.IsOffsetEnabled(alarm, 15));
        Assert.IsFalse(AlarmScheduleCalculator.IsOffsetEnabled(alarm, 5));
        Assert.IsTrue(AlarmScheduleCalculator.IsOffsetEnabled(alarm, 1));
        Assert.IsFalse(AlarmScheduleCalculator.IsOffsetEnabled(alarm, 0), "at-start disabled");
    }

    [TestMethod]
    public void IsOffsetEnabled_StartTimeCanBeEnabledIndependentlyOfEarlierNotifications()
    {
        // Before-reminders all off, but at-start on: only offset 0 is enabled.
        var alarm = new EventAlarm(true, false, false, false, NotifyAtStart: true);

        Assert.IsFalse(AlarmScheduleCalculator.IsOffsetEnabled(alarm, 15));
        Assert.IsFalse(AlarmScheduleCalculator.IsOffsetEnabled(alarm, 5));
        Assert.IsFalse(AlarmScheduleCalculator.IsOffsetEnabled(alarm, 1));
        Assert.IsTrue(AlarmScheduleCalculator.IsOffsetEnabled(alarm, 0));
    }

    [TestMethod]
    public void IsDue_BeforeScheduledTime_DoesNotRing()
    {
        var notifyAt = new DateTime(2026, 6, 10, 9, 19, 0);
        Assert.IsFalse(AlarmScheduleCalculator.IsDue(notifyAt.AddSeconds(-1), notifyAt));
        Assert.IsFalse(AlarmScheduleCalculator.IsDue(notifyAt.AddMinutes(-2), notifyAt), "must not fire 2 min early");
    }

    [TestMethod]
    public void IsDue_AtScheduledTimeAndWithinGrace_Rings()
    {
        var notifyAt = new DateTime(2026, 6, 10, 9, 19, 0);
        Assert.IsTrue(AlarmScheduleCalculator.IsDue(notifyAt, notifyAt), "exactly on time");
        Assert.IsTrue(AlarmScheduleCalculator.IsDue(notifyAt.AddSeconds(30), notifyAt), "within catch-up");
        Assert.IsTrue(AlarmScheduleCalculator.IsDue(notifyAt.Add(AlarmScheduleCalculator.CatchUpGrace), notifyAt), "grace boundary");
    }

    [TestMethod]
    public void IsDue_AfterGrace_DoesNotRing()
    {
        var notifyAt = new DateTime(2026, 6, 10, 9, 19, 0);
        Assert.IsFalse(AlarmScheduleCalculator.IsDue(notifyAt.Add(AlarmScheduleCalculator.CatchUpGrace).AddSeconds(1), notifyAt));
    }

    [TestMethod]
    public void Scenario_EventStartingAt0919_RingsStartTimeNotificationAt0919()
    {
        // Event starts 9:19, default alarm (at-start enabled). At 9:18 (creation) it is NOT due;
        // at 9:19 it becomes due. Reproduces the reported "doesn't fire at 9:19" case.
        var occStart = new DateTime(2026, 6, 10, 9, 19, 0);
        var atStart = occStart.AddMinutes(0);

        Assert.IsTrue(AlarmScheduleCalculator.IsOffsetEnabled(EventAlarm.Default, 0));
        Assert.IsFalse(AlarmScheduleCalculator.IsDue(new DateTime(2026, 6, 10, 9, 18, 30), atStart), "not at 9:18");
        Assert.IsTrue(AlarmScheduleCalculator.IsDue(new DateTime(2026, 6, 10, 9, 19, 5), atStart), "fires at 9:19");
    }
}
