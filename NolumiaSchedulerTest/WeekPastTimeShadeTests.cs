using Microsoft.UI.Xaml;
using NolumiaScheduler.Presentation.Helpers;
using NolumiaScheduler.Presentation.ViewModels;

namespace NolumiaSchedulerTest;

/// <summary>
/// Shading of the part of the week grid that has already elapsed. The grid is laid out one
/// pixel per minute, so these heights are also minutes from midnight.
/// </summary>
[TestClass]
public class WeekPastTimeShadeTests
{
    private const double DayHeight = 24 * 60;

    /// <summary>
    /// The heights are whole minutes carried in a double, so any real difference is far larger
    /// than this. Named rather than inline because MSTest wants an explicit tolerance to compare
    /// doubles at all.
    /// </summary>
    private const double Tolerance = 1e-9;

    private static WeekDayColumn Column(bool isToday = false, bool isPast = false)
        => new("Mon 4", new DateTime(2026, 5, 4), isHoliday: false, isToday: isToday, isPast: isPast);

    /// <summary>09:30 as minutes from midnight.</summary>
    private const double HalfPastNine = (9 * 60) + 30;

    [TestMethod]
    public void PastDay_IsShadedForWholeDay()
    {
        var column = Column(isPast: true);

        Assert.AreEqual(DayHeight, column.PastShadeHeight(HalfPastNine, DayHeight), Tolerance);
    }

    [TestMethod]
    public void Today_IsShadedUpToCurrentTime()
    {
        var column = Column(isToday: true);

        Assert.AreEqual(HalfPastNine, column.PastShadeHeight(HalfPastNine, DayHeight), Tolerance);
    }

    [TestMethod]
    public void FutureDay_IsNotShaded()
    {
        var column = Column();

        Assert.AreEqual(0d, column.PastShadeHeight(HalfPastNine, DayHeight), Tolerance);
    }

    [TestMethod]
    public void Today_JustBeforeMidnight_ShadeDoesNotExceedOneDay()
    {
        var column = Column(isToday: true);

        // The clock reading is pushed in from outside, so it must be clamped rather than
        // trusted: a stale value must not stretch the shade past the bottom of the column.
        Assert.AreEqual(DayHeight, column.PastShadeHeight(DayHeight + 120, DayHeight), Tolerance);
    }

    [TestMethod]
    public void Today_AtMidnight_IsNotShaded()
    {
        var column = Column(isToday: true);

        Assert.AreEqual(0d, column.PastShadeHeight(0, DayHeight), Tolerance);
        Assert.AreEqual(0d, column.PastShadeHeight(-5, DayHeight), Tolerance);
    }

    [TestMethod]
    public void Today_IsNotAPastDay()
    {
        var column = Column(isToday: true);

        Assert.IsFalse(column.IsPast);
    }

    // Theme state is process-wide, so the theme-dependent tests must not run in parallel with
    // each other (this assembly parallelizes at method level).
    [TestMethod]
    [DoNotParallelize]
    public void ShadeColor_UsesSameTokenAsMonthView()
    {
        Assert.AreEqual(WinColors.GCalPastDayShade, WeekDayColumn.PastShadeColor);
    }

    [TestMethod]
    [DoNotParallelize]
    public void ShadeColor_HasDedicatedOpacityInDarkTheme()
    {
        ThemeHelper.UpdateTheme(ElementTheme.Dark);
        try
        {
            Assert.AreEqual(WinColors.GCalPastDayShadeDark, WeekDayColumn.PastShadeColor);
        }
        finally
        {
            // Restore the default so other tests keep seeing light mode.
            ThemeHelper.UpdateTheme(ElementTheme.Default);
        }
    }

    [TestMethod]
    [DoNotParallelize]
    public void Shade_IsTranslucent_BackgroundAndTimeLinesShowThrough()
    {
        int alpha = WeekDayColumn.PastShadeColor.A;

        Assert.IsGreaterThan(0, alpha);
        Assert.IsLessThan(255, alpha);
    }
}
