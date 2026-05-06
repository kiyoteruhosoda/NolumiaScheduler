using NolumiaScheduler.Domain.ValueObjects;
using NolumiaScheduler.Presentation.Services;
using NolumiaScheduler.Presentation.ViewModels;

namespace NolumiaSchedulerTest;

[TestClass]
public class WeekViewPresentationTests
{
    [TestMethod]
    public void 時間帯イベントが開始時刻の分位置に配置される()
    {
        var strategy = new DefaultWeekEventLayoutStrategy();
        var blocks = strategy.Layout([
            CreateItem("e1", 9, 30, 10, 30, false),
        ]);

        Assert.HasCount(1, blocks);
        Assert.AreEqual(570d, blocks[0].Top);
        Assert.AreEqual(60d, blocks[0].Height);
    }

    [TestMethod]
    public void 終日イベントは時間グリッドに表示しない()
    {
        var strategy = new DefaultWeekEventLayoutStrategy();
        var blocks = strategy.Layout([CreateItem("all", null, null, null, null, true)]);

        Assert.HasCount(0, blocks);
    }

    [TestMethod]
    public void 休日カラムは月表示と同系統の赤背景になる()
    {
        var holidayColumn = new WeekDayColumn("Sun 4", new DateTime(2026,5,4), true);
        var normalColumn = new WeekDayColumn("Mon 5", new DateTime(2026,5,5), false);

        Assert.AreEqual("#FFF0F0", holidayColumn.DayBackgroundColor.ToArgbHex());
        Assert.AreEqual(Colors.Transparent, normalColumn.DayBackgroundColor);
    }


    [TestMethod]
    public void 月表示と週表示で同一Occurrenceの時間情報が一致する()
    {
        var item = CreateItem("sync", 14, 15, 15, 0, false);
        Assert.AreEqual("14:15 – 15:00", item.TimeRange);
        Assert.AreEqual(855, item.StartMinuteOfDay);
        Assert.AreEqual(900, item.EndMinuteOfDay);
    }


    [TestMethod]
    public void 重ならない予定は幅100パーセントになる()
    {
        var strategy = new DefaultWeekEventLayoutStrategy();
        var blocks = strategy.Layout([
            CreateItem("a", 9, 0, 10, 0, false),
            CreateItem("b", 10, 0, 11, 0, false),
        ]);

        Assert.IsTrue(blocks.All(b => Math.Abs(b.WidthRatio - 1d) < 0.0001));
    }

    [TestMethod]
    public void 完全重複2件は幅50パーセントずつになる()
    {
        var strategy = new DefaultWeekEventLayoutStrategy();
        var blocks = strategy.Layout([
            CreateItem("a", 9, 0, 10, 0, false),
            CreateItem("b", 9, 0, 10, 0, false),
        ]);

        Assert.IsTrue(blocks.All(b => Math.Abs(b.WidthRatio - 0.5d) < 0.0001));
    }

    [TestMethod]
    public void 三重複は幅3分の1ずつになる()
    {
        var strategy = new DefaultWeekEventLayoutStrategy();
        var blocks = strategy.Layout([
            CreateItem("a", 9, 0, 11, 0, false),
            CreateItem("b", 9, 30, 10, 30, false),
            CreateItem("c", 9, 45, 10, 15, false),
        ]);

        Assert.IsTrue(blocks.All(b => Math.Abs(b.WidthRatio - (1d / 3d)) < 0.0001));
    }

    [TestMethod]
    public void 連続予定は重複扱いしない()
    {
        var strategy = new DefaultWeekEventLayoutStrategy();
        var blocks = strategy.Layout([
            CreateItem("a", 9, 0, 10, 0, false),
            CreateItem("b", 10, 0, 11, 0, false),
        ]);

        Assert.IsTrue(blocks.All(b => Math.Abs(b.LeftRatio) < 0.0001));
    }

    private static CalendarEventItem CreateItem(string id, int? sh, int? sm, int? eh, int? em, bool allDay)
    {
        var occ = new EventOccurrence(
            new EventId(id),
            new LocalDateValue(2026, 5, 4),
            sh.HasValue && sm.HasValue ? new LocalTimeValue(sh.Value, sm.Value) : null,
            eh.HasValue && em.HasValue ? new LocalTimeValue(eh.Value, em.Value) : null,
            allDay,
            new EventTitle("sample"),
            null,
            NolumiaScheduler.Domain.ValueObjects.Visibility.Public);

        return new CalendarEventItem(occ);
    }
}
