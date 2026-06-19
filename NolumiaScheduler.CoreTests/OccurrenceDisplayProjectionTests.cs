using NolumiaScheduler.Domain.Services;
using NolumiaScheduler.Domain.ValueObjects;

namespace NolumiaScheduler.CoreTests;

/// <summary>
/// Display projection of a canonical occurrence into a viewer timezone (docs/time-model.md §4):
/// the displayed date/time changes per viewer (and per DST season) while the canonical identity
/// key is preserved for editing/moving.
/// </summary>
[TestClass]
public class OccurrenceDisplayProjectionTests
{
    private static readonly TimeZoneInfo Tokyo = new TimeZoneId("Asia/Tokyo").ToTimeZoneInfo();
    private static readonly TimeZoneInfo NewYork = new TimeZoneId("America/New_York").ToTimeZoneInfo();

    private static EventOccurrence Occ(int y, int mo, int d, int h, int mi, int durationMinutes, OccurrenceLocalKey? seriesKey = null)
        => new(new EventId("e1"), new LocalDateValue(y, mo, d), new LocalTimeValue(h, mi, 0),
            durationMinutes, new EventTitle("t"), null, Visibility.Public, seriesKey: seriesKey);

    [TestMethod]
    public void JstOccurrence_ProjectedToNewYork_ShiftsDateAndTime()
    {
        // 2026-06-15 10:00 JST == 2026-06-14 21:00 EDT.
        var disp = OccurrenceDisplayProjection.ToDisplayTimeZone(Occ(2026, 6, 15, 10, 0, 60), Tokyo, NewYork);

        Assert.AreEqual(new LocalDateValue(2026, 6, 14), disp.Date);
        Assert.AreEqual(new LocalTimeValue(21, 0, 0), disp.StartTime);
        Assert.AreEqual(60, disp.DurationMinutes);
    }

    [TestMethod]
    public void JstOccurrence_ProjectedToNewYork_DiffersBetweenSummerAndWinter()
    {
        var summer = OccurrenceDisplayProjection.ToDisplayTimeZone(Occ(2026, 7, 1, 10, 0, 60), Tokyo, NewYork);
        var winter = OccurrenceDisplayProjection.ToDisplayTimeZone(Occ(2026, 1, 15, 10, 0, 60), Tokyo, NewYork);

        Assert.AreEqual(new LocalTimeValue(21, 0, 0), summer.StartTime, "EDT");
        Assert.AreEqual(new LocalTimeValue(20, 0, 0), winter.StartTime, "EST");
    }

    [TestMethod]
    public void NewYorkRecurrence_ProjectedToTokyo_ShiftsOneHourAcrossDst()
    {
        // "09:00 America/New_York" recurrence: same NY wall-clock, but Tokyo display moves 1h.
        var summer = OccurrenceDisplayProjection.ToDisplayTimeZone(Occ(2026, 7, 1, 9, 0, 60), NewYork, Tokyo);
        var winter = OccurrenceDisplayProjection.ToDisplayTimeZone(Occ(2026, 1, 15, 9, 0, 60), NewYork, Tokyo);

        Assert.AreEqual(new LocalTimeValue(22, 0, 0), summer.StartTime, "09:00 EDT == 22:00 JST");
        Assert.AreEqual(new LocalTimeValue(23, 0, 0), winter.StartTime, "09:00 EST == 23:00 JST");
    }

    [TestMethod]
    public void Projection_PreservesCanonicalKey_ForRecurringSeries()
    {
        var seriesKey = new OccurrenceLocalKey(new LocalDateValue(2026, 6, 15), new LocalTimeValue(10, 0, 0));
        var disp = OccurrenceDisplayProjection.ToDisplayTimeZone(Occ(2026, 6, 15, 10, 0, 60, seriesKey), Tokyo, NewYork);

        // Display moved to the prior NY day, but identity is still the event-local series key.
        Assert.AreEqual(new LocalDateValue(2026, 6, 14), disp.Date);
        Assert.AreEqual(seriesKey, disp.SeriesKey);
    }

    [TestMethod]
    public void Projection_ForSingleOccurrence_SetsCanonicalKeyToEventLocal()
    {
        var disp = OccurrenceDisplayProjection.ToDisplayTimeZone(Occ(2026, 6, 15, 10, 0, 60), Tokyo, NewYork);

        Assert.IsNotNull(disp.SeriesKey);
        Assert.AreEqual(new LocalDateValue(2026, 6, 15), disp.SeriesKey!.Date);
        Assert.AreEqual(new LocalTimeValue(10, 0, 0), disp.SeriesKey.Time);
    }

    [TestMethod]
    public void SameTimeZone_ReturnsEquivalentOccurrence()
    {
        var disp = OccurrenceDisplayProjection.ToDisplayTimeZone(Occ(2026, 6, 15, 10, 0, 60), Tokyo, Tokyo);

        Assert.AreEqual(new LocalDateValue(2026, 6, 15), disp.Date);
        Assert.AreEqual(new LocalTimeValue(10, 0, 0), disp.StartTime);
    }

    [TestMethod]
    public void AllDayOccurrence_IsNotShiftedAcrossZones()
    {
        // All-day (00:00 + 24h) is floating: it must stay on its own date, not slide into NY's
        // previous day.
        var disp = OccurrenceDisplayProjection.ToDisplayTimeZone(Occ(2026, 6, 15, 0, 0, 24 * 60), Tokyo, NewYork);

        Assert.AreEqual(new LocalDateValue(2026, 6, 15), disp.Date);
        Assert.AreEqual(new LocalTimeValue(0, 0, 0), disp.StartTime);
        Assert.AreEqual(24 * 60, disp.DurationMinutes);
    }
}
