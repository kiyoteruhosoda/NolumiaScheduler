using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.Services;
using NolumiaScheduler.Domain.ValueObjects;

namespace NolumiaSchedulerTest;

[TestClass]
public class EventColorTests
{
    private static CalendarEvent CreateSingleEvent(EventColorKey colorKey = EventColorKey.Default)
    {
        var start = new DateTimeOffset(2026, 6, 10, 9, 0, 0, TimeSpan.FromHours(9));
        return CalendarEvent.CreateSingle(
            new EventId("ev-1"),
            new EventTitle("Meeting"),
            location: null,
            visibility: Visibility.Public,
            eventType: null,
            description: null,
            new TimeZoneId("Asia/Tokyo"),
            new SingleEventSchedule(start.ToUniversalTime(), 60),
            createdAt: start,
            colorKey: colorKey);
    }

    [TestMethod]
    public void 色を指定しない場合はDefaultになる()
    {
        var ev = CreateSingleEvent();
        Assert.AreEqual(EventColorKey.Default, ev.ColorKey);
    }

    [TestMethod]
    public void 生成時に指定した色が保持される()
    {
        var ev = CreateSingleEvent(EventColorKey.Tomato);
        Assert.AreEqual(EventColorKey.Tomato, ev.ColorKey);
    }

    [TestMethod]
    public void SetColorで色が変わりVersionが進む()
    {
        var ev = CreateSingleEvent();
        var versionBefore = ev.Version.Value;

        ev.SetColor(EventColorKey.Basil, DateTimeOffset.UtcNow);

        Assert.AreEqual(EventColorKey.Basil, ev.ColorKey);
        Assert.AreEqual(versionBefore + 1, ev.Version.Value);
    }

    [TestMethod]
    public void SetColorで同じ色を設定してもVersionは進まない()
    {
        var ev = CreateSingleEvent(EventColorKey.Basil);
        var versionBefore = ev.Version.Value;

        ev.SetColor(EventColorKey.Basil, DateTimeOffset.UtcNow);

        Assert.AreEqual(versionBefore, ev.Version.Value);
    }

    [TestMethod]
    public void 展開したオカレンスへ色が伝播する()
    {
        var ev = CreateSingleEvent(EventColorKey.Peacock);
        var expander = new OccurrenceExpander(new BusinessDayShiftService());

        var occurrences = expander.Expand(
            ev,
            new LocalDateValue(2026, 6, 1),
            new LocalDateValue(2026, 6, 30),
            businessCalendar: null);

        Assert.HasCount(1, occurrences);
        Assert.AreEqual(EventColorKey.Peacock, occurrences[0].ColorKey);
    }
}
