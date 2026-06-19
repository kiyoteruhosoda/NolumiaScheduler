using NolumiaScheduler.Domain.ValueObjects;

namespace NolumiaScheduler.CoreTests;

/// <summary>
/// Timezone / DST behaviour of the wall-clock → instant resolution that the whole time model
/// rests on (docs/time-model.md). These assert the absolute instants and how the same nominal
/// wall-clock time renders in another zone, including across daylight-saving transitions.
/// </summary>
[TestClass]
public class LocalSchedulePointTests
{
    private static readonly TimeZoneInfo Tokyo = new TimeZoneId("Asia/Tokyo").ToTimeZoneInfo();
    private static readonly TimeZoneInfo NewYork = new TimeZoneId("America/New_York").ToTimeZoneInfo();

    private static DateTimeOffset StartInstant(int y, int m, int d, int h, int min, TimeZoneInfo tz)
        => LocalSchedulePoint.StartInstant(new LocalDateValue(y, m, d), new LocalTimeValue(h, min, 0), tz);

    // ── A JST reservation is a fixed absolute instant ───────────────────────────────────────

    [TestMethod]
    public void JstWallClock_ResolvesToFixedUtcInstant()
    {
        // Asia/Tokyo is UTC+9 year-round (no DST).
        var instant = StartInstant(2026, 6, 15, 10, 0, Tokyo);
        Assert.AreEqual(new DateTimeOffset(2026, 6, 15, 1, 0, 0, TimeSpan.Zero), instant.ToUniversalTime());
    }

    [TestMethod]
    public void JstReservation_ShownInNewYork_HasDifferentWallClock()
    {
        // 2026-06-15 10:00 JST == 01:00 UTC. New York is on EDT (UTC-4) in June.
        var instant = StartInstant(2026, 6, 15, 10, 0, Tokyo);
        var inNewYork = TimeZoneInfo.ConvertTime(instant, NewYork);
        Assert.AreEqual(new DateTime(2026, 6, 14, 21, 0, 0), inNewYork.DateTime);
    }

    // ── Same JST nominal time renders differently in NY across NY's summer/winter ───────────

    [TestMethod]
    public void JstReservation_ShownInNewYork_DiffersBetweenSummerAndWinter()
    {
        // Same wall-clock "10:00 JST", two dates. New York: EDT (UTC-4) in summer, EST (UTC-5) in winter.
        var summer = TimeZoneInfo.ConvertTime(StartInstant(2026, 7, 1, 10, 0, Tokyo), NewYork);
        var winter = TimeZoneInfo.ConvertTime(StartInstant(2026, 1, 15, 10, 0, Tokyo), NewYork);

        Assert.AreEqual(new TimeSpan(21, 0, 0), summer.TimeOfDay, "summer (EDT): 10:00 JST shows as 21:00 prev day");
        Assert.AreEqual(new TimeSpan(20, 0, 0), winter.TimeOfDay, "winter (EST): 10:00 JST shows as 20:00 prev day");
        Assert.AreEqual(1, (summer.TimeOfDay - winter.TimeOfDay).TotalHours);
    }

    // ── A recurring NY wall-clock keeps its NY time but shifts 1h in Japan across DST ────────

    [TestMethod]
    public void NewYorkWallClock_KeepsLocalTime_ButShiftsOneHourInTokyoAcrossDst()
    {
        // Recurring "09:00 America/New_York" anchored to NY wall-clock (docs/time-model.md §4-2).
        var summerInstant = StartInstant(2026, 7, 1, 9, 0, NewYork);   // EDT (UTC-4) → 13:00 UTC
        var winterInstant = StartInstant(2026, 1, 15, 9, 0, NewYork);  // EST (UTC-5) → 14:00 UTC

        // In New York the wall-clock is unchanged across seasons...
        Assert.AreEqual(new TimeSpan(9, 0, 0), TimeZoneInfo.ConvertTime(summerInstant, NewYork).TimeOfDay);
        Assert.AreEqual(new TimeSpan(9, 0, 0), TimeZoneInfo.ConvertTime(winterInstant, NewYork).TimeOfDay);

        // ...but the absolute instant moves by 1h, so Tokyo (no DST) shows a 1h difference.
        var summerInTokyo = TimeZoneInfo.ConvertTime(summerInstant, Tokyo);
        var winterInTokyo = TimeZoneInfo.ConvertTime(winterInstant, Tokyo);
        Assert.AreEqual(new TimeSpan(22, 0, 0), summerInTokyo.TimeOfDay, "summer: 09:00 EDT == 22:00 JST");
        Assert.AreEqual(new TimeSpan(23, 0, 0), winterInTokyo.TimeOfDay, "winter: 09:00 EST == 23:00 JST");
    }

    // ── Duration is wall-clock: a fixed-length event keeps its length across a DST jump ──────

    [TestMethod]
    public void Duration_IsWallClock_AcrossSpringForward()
    {
        // US DST 2026 starts 2026-03-08 02:00 (clocks jump to 03:00). A 2h wall-clock event from
        // 01:00 ends at "03:00" wall-clock, i.e. only 1 real hour elapses.
        var date = new LocalDateValue(2026, 3, 8);
        var start = LocalSchedulePoint.StartInstant(date, new LocalTimeValue(1, 0, 0), NewYork);
        var end = LocalSchedulePoint.EndInstant(date, new LocalTimeValue(1, 0, 0), 120, NewYork);

        // Wall-clock end is 03:00 local.
        Assert.AreEqual(new TimeSpan(3, 0, 0), TimeZoneInfo.ConvertTime(end, NewYork).TimeOfDay);
        // Real elapsed time is 1 hour because the 02:00–03:00 wall-clock hour does not exist.
        Assert.AreEqual(1, (end - start).TotalHours);
    }
}
