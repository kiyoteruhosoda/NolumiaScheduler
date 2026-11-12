using NolumiaScheduler.Presentation.Services;
using Windows.Foundation;

namespace NolumiaSchedulerTest;

[TestClass]
public class WeekInteractionServicesTests
{
    [TestMethod]
    public void TapAndDrag_AreNotMisclassified()
    {
        var sut = new WeekGestureArbitrationService();
        var tap = sut.Decide(true, false, new Point(0, 0), new Point(3, 3), TimeSpan.FromMilliseconds(100));
        var drag = sut.Decide(true, false, new Point(0, 0), new Point(12, 1), TimeSpan.FromMilliseconds(200));

        Assert.AreEqual(WeekGestureDecision.Tap, tap);
        Assert.AreEqual(WeekGestureDecision.Drag, drag);
    }

    [TestMethod]
    public void Resize_PrefersVerticalDirection()
    {
        var sut = new WeekGestureArbitrationService();
        var decision = sut.Decide(true, true, new Point(0, 0), new Point(1, 12), TimeSpan.FromMilliseconds(300));
        Assert.AreEqual(WeekGestureDecision.Resize, decision);
    }

    [TestMethod]
    public void TapCreate_RoundsMinute15And45To0And30()
    {
        var sut = new WeekInteractionMapper();

        // Snaps to the start of the tapped half-hour slot (3c362fa): 9:10/9:20 -> 9:00, 9:40/9:50 -> 9:30
        Assert.AreEqual(9 * 60, sut.MapToHalfHourMinute(9 * 60 + 10));
        Assert.AreEqual(9 * 60, sut.MapToHalfHourMinute(9 * 60 + 20));
        Assert.AreEqual(9 * 60 + 30, sut.MapToHalfHourMinute(9 * 60 + 40));
        Assert.AreEqual(9 * 60 + 30, sut.MapToHalfHourMinute(9 * 60 + 50));
    }

    [TestMethod]
    public void TapCreate_DoesNotProduce15Or45MinuteBoundaries()
    {
        var sut = new WeekInteractionMapper();
        for (var minute = 0; minute <= 1439; minute++)
        {
            var snapped = sut.MapToHalfHourMinute(minute);
            Assert.AreNotEqual(15, snapped % 60, $"{minute} min snapped to 15 min");
            Assert.AreNotEqual(45, snapped % 60, $"{minute} min snapped to 45 min");
        }
    }

    [TestMethod]
    public void NearEdge_ProducesAutoScrollAmount()
    {
        var sut = new WeekAutoScrollService();
        var top = sut.ComputeVerticalDelta(4, 600);
        var center = sut.ComputeVerticalDelta(300, 600);
        var bottom = sut.ComputeVerticalDelta(598, 600);

        Assert.IsLessThan(0d, top);
        Assert.AreEqual(0, center);
        Assert.IsGreaterThan(0d, bottom);
    }
}
