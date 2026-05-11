using NolumiaScheduler.Domain.ValueObjects;
using NolumiaScheduler.Presentation.Services;
using NolumiaScheduler.Presentation.ViewModels;

namespace NolumiaSchedulerTest;

[TestClass]
public class WeekViewPresentationTests
{
    private const double EventColumnGapRatio = 0.015d;

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

        Assert.IsTrue(blocks.All(b => Math.Abs(b.WidthRatio - ExpectedWidthRatio(2)) < 0.0001));
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

        Assert.IsTrue(blocks.All(b => Math.Abs(b.WidthRatio - ExpectedWidthRatio(3)) < 0.0001));
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


    [TestMethod]
    public void 連鎖重複は実際の同時件数に応じて幅を使い切る()
    {
        var strategy = new DefaultWeekEventLayoutStrategy();
        var blocks = strategy.Layout([
            CreateItem("a", 10, 0, 11, 0, false),
            CreateItem("b", 10, 30, 11, 30, false),
            CreateItem("c", 11, 0, 12, 0, false),
        ]).ToDictionary(x => x.EventId);

        Assert.IsLessThan(0.02, Math.Abs(blocks["a"].WidthRatio - ExpectedWidthRatio(2)));
        Assert.IsLessThan(0.02, Math.Abs(blocks["b"].WidthRatio - ExpectedWidthRatio(3)));
        Assert.IsLessThan(0.02, Math.Abs(blocks["c"].WidthRatio - ExpectedWidthRatio(2)));
    }

    [TestMethod]
    public void 長い予定と短い予定の組み合わせでも右側の空白列を作らない()
    {
        var strategy = new DefaultWeekEventLayoutStrategy();
        var blocks = strategy.Layout([
            CreateItem("long", 9, 0, 12, 0, false),
            CreateItem("short1", 9, 30, 10, 0, false),
            CreateItem("short2", 10, 30, 11, 0, false),
        ]).ToDictionary(x => x.EventId);

        Assert.IsLessThan(0.02, Math.Abs(blocks["long"].WidthRatio - ExpectedWidthRatio(3)));
        Assert.IsLessThan(0.02, Math.Abs(blocks["short1"].WidthRatio - ExpectedWidthRatio(2)));
        Assert.IsLessThan(0.02, Math.Abs(blocks["short2"].WidthRatio - ExpectedWidthRatio(2)));
    }
    [TestMethod]
    public void 終日イベントは終日レーンに配置される()
    {
        var strategy = new DefaultWeekAllDayLayoutStrategy();
        var blocks = strategy.Layout([
            CreateItem("all", null, null, null, null, true)
        ], new DateTime(2026, 5, 3));

        Assert.HasCount(1, blocks);
        Assert.AreEqual(1, blocks[0].WidthColumns);
        Assert.AreEqual(1, blocks[0].LeftColumn);
    }

    [TestMethod]
    public void 終日イベントが重なる場合はスタック表示になる()
    {
        var strategy = new DefaultWeekAllDayLayoutStrategy();
        var blocks = strategy.Layout([
            CreateItem("a", null, null, null, null, true),
            CreateItem("b", null, null, null, null, true),
        ], new DateTime(2026, 5, 3));

        Assert.AreEqual(0, blocks[0].Row);
        Assert.AreEqual(1, blocks[1].Row);
    }

    [TestMethod]
    public void 日跨ぎ時間予定は分割対象フラグを持つ()
    {
        var item = CreateItem("overnight", 22, 0, 2, 0, false);
        Assert.IsTrue(item.CrossesMidnight);
    }

    private static double ExpectedWidthRatio(int columns)
        => (1d - EventColumnGapRatio * (columns - 1)) / columns;

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
