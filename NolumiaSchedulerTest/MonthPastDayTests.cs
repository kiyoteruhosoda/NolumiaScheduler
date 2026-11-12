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
    public void PastDayCell_IsShaded()
    {
        var cell = CreateCell(day: 4, isPast: true);

        Assert.AreEqual(WinColors.GCalPastDayShade, cell.PastShadeColor);
    }

    [TestMethod]
    public void FutureDayCell_IsNotShaded()
    {
        var cell = CreateCell(day: 20, isPast: false);

        Assert.AreEqual(WinColors.Transparent, cell.PastShadeColor);
    }

    [TestMethod]
    public void Today_IsNotPast_IsNotShaded()
    {
        var cell = CreateCell(day: 10, isPast: false, isToday: true);

        Assert.AreEqual(WinColors.Transparent, cell.PastShadeColor);
    }

    [TestMethod]
    [DoNotParallelize]
    public void PastDayShade_IsTranslucent_BackgroundShowsThrough()
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
    public void PastDayShade_HasDedicatedOpacityInDarkTheme()
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
