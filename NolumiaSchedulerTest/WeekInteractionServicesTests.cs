using NolumiaScheduler.Presentation.Services;
using Microsoft.Maui.Graphics;

namespace NolumiaSchedulerTest;

[TestClass]
public class WeekInteractionServicesTests
{
    [TestMethod]
    public void タップとドラッグの誤判定を防止する()
    {
        var sut = new WeekGestureArbitrationService();
        var tap = sut.Decide(true, false, new Point(0, 0), new Point(3, 3), TimeSpan.FromMilliseconds(100));
        var drag = sut.Decide(true, false, new Point(0, 0), new Point(12, 1), TimeSpan.FromMilliseconds(200));

        Assert.AreEqual(WeekGestureDecision.Tap, tap);
        Assert.AreEqual(WeekGestureDecision.Drag, drag);
    }

    [TestMethod]
    public void リサイズは縦方向優先で判定される()
    {
        var sut = new WeekGestureArbitrationService();
        var decision = sut.Decide(true, true, new Point(0, 0), new Point(1, 12), TimeSpan.FromMilliseconds(300));
        Assert.AreEqual(WeekGestureDecision.Resize, decision);
    }

    [TestMethod]
    public void エッジ近傍でオートスクロール量が発生する()
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
