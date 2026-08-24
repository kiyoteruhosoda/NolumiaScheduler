using Microsoft.UI.Xaml;
using NolumiaScheduler.Domain.ValueObjects;
using NolumiaScheduler.Presentation.Helpers;
using NolumiaScheduler.Presentation.ViewModels;

namespace NolumiaSchedulerTest;

/// <summary>
/// Month-grid shading of days that have already passed.
/// </summary>
[TestClass]
public class MonthPastDayTests
{
    private static CalendarDayCell CreateCell(int day, bool isPast, bool isToday = false)
        => new()
        {
            Date = new LocalDateValue(2026, 5, day),
            IsPast = isPast,
            IsToday = isToday,
            IsCurrentMonth = true,
        };

    [TestMethod]
    [DoNotParallelize]
    public void 過去日のセルには影が敷かれる()
    {
        var cell = CreateCell(day: 4, isPast: true);

        Assert.AreEqual(WinColors.GCalPastDayShade, cell.PastShadeColor);
    }

    [TestMethod]
    public void 未来日のセルには影が敷かれない()
    {
        var cell = CreateCell(day: 20, isPast: false);

        Assert.AreEqual(WinColors.Transparent, cell.PastShadeColor);
    }

    [TestMethod]
    public void 当日は過去日ではないので影が敷かれない()
    {
        var cell = CreateCell(day: 10, isPast: false, isToday: true);

        Assert.AreEqual(WinColors.Transparent, cell.PastShadeColor);
    }

    [TestMethod]
    [DoNotParallelize]
    public void 過去日の影は半透明なので下地の背景が透ける()
    {
        var cell = CreateCell(day: 4, isPast: true);

        int alpha = cell.PastShadeColor.A;
        Assert.IsGreaterThan(0, alpha);
        Assert.IsLessThan(255, alpha);
    }

    // Theme state is process-wide, so this must not run beside the light-theme tests above
    // (this assembly parallelizes at method level).
    [TestMethod]
    [DoNotParallelize]
    public void 過去日の影はダークテーマでは専用の濃さになる()
    {
        ThemeHelper.UpdateTheme(ElementTheme.Dark);
        try
        {
            var cell = CreateCell(day: 4, isPast: true);

            Assert.AreEqual(WinColors.GCalPastDayShadeDark, cell.PastShadeColor);
        }
        finally
        {
            // Restore the default so other tests keep seeing light mode.
            ThemeHelper.UpdateTheme(ElementTheme.Default);
        }
    }
}
