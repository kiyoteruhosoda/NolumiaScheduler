using Microsoft.UI.Xaml;
using NolumiaScheduler.Domain.ValueObjects;
using NolumiaScheduler.Presentation.Helpers;
using NolumiaScheduler.Presentation.Services;
using NolumiaScheduler.Presentation.ViewModels;

namespace NolumiaSchedulerTest;

[TestClass]
public class WeekViewPresentationTests
{
    private const double EventColumnGapRatio = 0.015d;

    [TestMethod]
    public void TimedEvent_IsPlacedAtStartMinutePosition()
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
    public void AllDayEvent_IsNotShownInTimeGrid()
    {
        var strategy = new DefaultWeekEventLayoutStrategy();
        var blocks = strategy.Layout([CreateItem("all", null, null, null, null, true)]);

        Assert.HasCount(0, blocks);
    }

    // Theme-dependent tests read the process-wide ThemeHelper state, so they must not run in
    // parallel with the dark-theme test (this assembly parallelizes at method level).
    [TestMethod]
    [DoNotParallelize]
    public void HolidayColumn_HasRedBackgroundMatchingMonthView()
    {
        var holidayColumn = new WeekDayColumn("Sun 4", new DateTime(2026,5,4), true);
        var normalColumn = new WeekDayColumn("Mon 5", new DateTime(2026,5,5), false);

        Assert.AreEqual("#FFF0F0", $"#{holidayColumn.DayBackgroundColor.R:X2}{holidayColumn.DayBackgroundColor.G:X2}{holidayColumn.DayBackgroundColor.B:X2}");
        Assert.AreEqual(WinColors.Transparent, normalColumn.DayBackgroundColor);
    }

    [TestMethod]
    [DoNotParallelize]
    public void HolidayColumn_HasDarkRedBackgroundInDarkTheme()
    {
        // Theme state is static: restore the default so other tests keep seeing light mode.
        ThemeHelper.UpdateTheme(ElementTheme.Dark);
        try
        {
            var holidayColumn = new WeekDayColumn("Sun 4", new DateTime(2026, 5, 4), true);
            Assert.AreEqual(WinColors.GCalHolidayBgDark, holidayColumn.DayBackgroundColor);
        }
        finally
        {
            ThemeHelper.UpdateTheme(ElementTheme.Default);
        }
    }


    [TestMethod]
    public void MonthAndWeekViews_ShowSameTimesForSameOccurrence()
    {
        var item = CreateItem("sync", 14, 15, 15, 0, false);
        Assert.AreEqual("14:15 – 15:00", item.TimeRange);
        Assert.AreEqual(855, item.StartMinuteOfDay);
        Assert.AreEqual(900, item.EndMinuteOfDay);
    }


    [TestMethod]
    public void NonOverlappingEvent_Is100PercentWide()
    {
        var strategy = new DefaultWeekEventLayoutStrategy();
        var blocks = strategy.Layout([
            CreateItem("a", 9, 0, 10, 0, false),
            CreateItem("b", 10, 0, 11, 0, false),
        ]);

        Assert.IsTrue(blocks.All(b => Math.Abs(b.WidthRatio - 1d) < 0.0001));
    }

    [TestMethod]
    public void TwoFullyOverlappingEvents_Are50PercentEach()
    {
        var strategy = new DefaultWeekEventLayoutStrategy();
        var blocks = strategy.Layout([
            CreateItem("a", 9, 0, 10, 0, false),
            CreateItem("b", 9, 0, 10, 0, false),
        ]);

        Assert.IsTrue(blocks.All(b => Math.Abs(b.WidthRatio - ExpectedWidthRatio(2)) < 0.0001));
    }

    [TestMethod]
    public void ThreeOverlappingEvents_AreOneThirdEach()
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
    public void BackToBackEvents_AreNotOverlapping()
    {
        var strategy = new DefaultWeekEventLayoutStrategy();
        var blocks = strategy.Layout([
            CreateItem("a", 9, 0, 10, 0, false),
            CreateItem("b", 10, 0, 11, 0, false),
        ]);

        Assert.IsTrue(blocks.All(b => Math.Abs(b.LeftRatio) < 0.0001));
    }


    [TestMethod]
    public void ChainedOverlap_UsesFullWidthByActualConcurrentCount()
    {
        var strategy = new DefaultWeekEventLayoutStrategy();
        var blocks = strategy.Layout([
            CreateItem("a", 10, 0, 11, 0, false),
            CreateItem("b", 10, 30, 11, 30, false),
            CreateItem("c", 11, 0, 12, 0, false),
        ]).ToDictionary(x => x.EventId);

        // At most 2 overlap at once, so even when chained every event gets a 2-column width
        Assert.IsLessThan(0.02, Math.Abs(blocks["a"].WidthRatio - ExpectedWidthRatio(2)));
        Assert.IsLessThan(0.02, Math.Abs(blocks["b"].WidthRatio - ExpectedWidthRatio(2)));
        Assert.IsLessThan(0.02, Math.Abs(blocks["c"].WidthRatio - ExpectedWidthRatio(2)));
    }

    [TestMethod]
    public void LongAndShortEventsMixed_LeaveNoEmptyColumnOnRight()
    {
        var strategy = new DefaultWeekEventLayoutStrategy();
        var blocks = strategy.Layout([
            CreateItem("long", 9, 0, 12, 0, false),
            CreateItem("short1", 9, 30, 10, 0, false),
            CreateItem("short2", 10, 30, 11, 0, false),
        ]).ToDictionary(x => x.EventId);

        Assert.IsLessThan(0.02, Math.Abs(blocks["long"].WidthRatio - ExpectedWidthRatio(2)));
        Assert.IsLessThan(0.02, Math.Abs(blocks["short1"].WidthRatio - ExpectedWidthRatio(2)));
        Assert.IsLessThan(0.02, Math.Abs(blocks["short2"].WidthRatio - ExpectedWidthRatio(2)));
    }

    [TestMethod]
    public void LongEventWithNonOverlappingNeighbors_DoesNotOverlapThemHorizontally()
    {
        // Event B (long) overlaps both A and C, but A and C do not overlap each other.
        // At most 2 overlap at once, so split into 2 columns: B on the left half, A/C on the right half.
        var strategy = new DefaultWeekEventLayoutStrategy();
        var blocks = strategy.Layout([
            CreateItem("A", 15, 0, 15, 30, false),
            CreateItem("B", 10, 0, 19, 30, false),
            CreateItem("C", 18, 0, 19, 0, false),
        ]).ToDictionary(x => x.EventId);

        Assert.IsLessThan(0.02, Math.Abs(blocks["B"].WidthRatio - ExpectedWidthRatio(2)));
        Assert.IsLessThan(0.02, Math.Abs(blocks["A"].WidthRatio - ExpectedWidthRatio(2)));
        Assert.IsLessThan(0.02, Math.Abs(blocks["C"].WidthRatio - ExpectedWidthRatio(2)));

        // B is at the left edge; A and C are placed right of B's right edge and do not overlap horizontally
        Assert.IsLessThan(0.0001, Math.Abs(blocks["B"].LeftRatio));
        var bRight = blocks["B"].LeftRatio + blocks["B"].WidthRatio;
        Assert.IsTrue(bRight <= blocks["A"].LeftRatio + 0.0001, "B overlaps A");
        Assert.IsTrue(bRight <= blocks["C"].LeftRatio + 0.0001, "B overlaps C");
    }
    [TestMethod]
    public void AllDayEvent_IsPlacedInAllDayLane()
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
    public void OverlappingAllDayEvents_AreStacked()
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
    public void CrossDayTimedEvent_HasSplitFlag()
    {
        var item = CreateItem("overnight", 22, 0, 2, 0, false);
        Assert.IsTrue(item.CrossesMidnight);
    }

    private static double ExpectedWidthRatio(int columns)
        => (1d - EventColumnGapRatio * (columns - 1)) / columns;

    private static CalendarEventItem CreateItem(string id, int? sh, int? sm, int? eh, int? em, bool allDay)
    {
        // The duration model has no all-day flag: a full-day occurrence is 00:00 + 1440 minutes.
        LocalTimeValue startTime;
        int durationMinutes;
        if (allDay)
        {
            startTime = new LocalTimeValue(0, 0, 0);
            durationMinutes = 24 * 60;
        }
        else
        {
            startTime = new LocalTimeValue(sh!.Value, sm!.Value);
            var startMin = (sh.Value * 60) + sm.Value;
            var endMin = (eh!.Value * 60) + em!.Value;
            // End on/before start is read as crossing midnight.
            durationMinutes = endMin > startMin ? endMin - startMin : endMin - startMin + (24 * 60);
        }

        var occ = new EventOccurrence(
            new EventId(id),
            new LocalDateValue(2026, 5, 4),
            startTime,
            durationMinutes,
            new EventTitle("sample"),
            null,
            NolumiaScheduler.Domain.ValueObjects.Visibility.Public);

        return new CalendarEventItem(occ);
    }
}
