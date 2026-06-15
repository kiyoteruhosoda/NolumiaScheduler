using NolumiaScheduler.Presentation.Services;
using Windows.Foundation;

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
    public void タップ作成は15分45分を0分30分に丸める()
    {
        var sut = new WeekInteractionMapper();

        // 9:10 -> 9:00, 9:20 -> 9:30, 9:40 -> 9:30, 9:50 -> 10:00
        Assert.AreEqual(9 * 60, sut.MapToHalfHourMinute(9 * 60 + 10));
        Assert.AreEqual(9 * 60 + 30, sut.MapToHalfHourMinute(9 * 60 + 20));
        Assert.AreEqual(9 * 60 + 30, sut.MapToHalfHourMinute(9 * 60 + 40));
        Assert.AreEqual(10 * 60, sut.MapToHalfHourMinute(9 * 60 + 50));
    }

    [TestMethod]
    public void タップ作成では15分45分の境界が生成されない()
    {
        var sut = new WeekInteractionMapper();
        for (var minute = 0; minute <= 1439; minute++)
        {
            var snapped = sut.MapToHalfHourMinute(minute);
            Assert.AreNotEqual(15, snapped % 60, $"{minute} 分が 15 分にスナップされました");
            Assert.AreNotEqual(45, snapped % 60, $"{minute} 分が 45 分にスナップされました");
        }
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
