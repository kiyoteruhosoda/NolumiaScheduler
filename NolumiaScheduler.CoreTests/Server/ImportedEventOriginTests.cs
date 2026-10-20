using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.Exceptions;
using NolumiaScheduler.Domain.Services;
using NolumiaScheduler.Domain.ValueObjects;
using NolumiaScheduler.Infrastructure.Server;

namespace NolumiaScheduler.CoreTests.Server;

/// <summary>
/// The origin of an event (task #550; the server's ADR-0057) travels from the server's imported
/// occurrence through the expander and the display projection to the occurrence the calendar's
/// parts (<c>CalendarEventItem</c>, <c>CalendarEventChip</c>, the week blocks) read their mark from.
/// </summary>
[TestClass]
public class ImportedEventOriginTests
{
    private static readonly TimeZoneInfo Tokyo = new TimeZoneId("Asia/Tokyo").ToTimeZoneInfo();
    private static readonly TimeZoneInfo NewYork = new TimeZoneId("America/New_York").ToTimeZoneInfo();
    private readonly OccurrenceExpander _expander = new(new BusinessDayShiftService());

    private static ImportedOccurrenceDto Imported(string? origin) => new(
        Id: "imported:9:0", CalendarId: 9, Title: "定例", Location: null,
        Start: new DateTimeOffset(2026, 11, 5, 1, 0, 0, TimeSpan.Zero), DurationMinutes: 60,
        IsAllDay: false, Origin: origin);

    private EventOccurrence Displayed(CalendarEvent ev, TimeZoneInfo viewer)
    {
        var occurrence = _expander.Expand(ev, new LocalDateValue(2026, 11, 1), new LocalDateValue(2026, 11, 30), null).Single();
        return OccurrenceDisplayProjection.ToDisplayTimeZone(occurrence, Tokyo, viewer);
    }

    [TestMethod]
    [DataRow("OUTLOOK", EventOrigin.Outlook)]
    [DataRow("FEED", EventOrigin.Feed)]
    [DataRow(null, EventOrigin.Outlook, DisplayName = "origin を返さないサーバーは Outlook とみなす")]
    public void 取り込んだ回の出どころが画面に渡る回まで届く(string? origin, EventOrigin expected)
    {
        var ev = ServerEventMapper.ToDomain(Imported(origin), "Asia/Tokyo");

        Assert.IsTrue(ev.IsReadOnly);
        Assert.AreEqual(expected, ev.Origin);
        Assert.AreEqual(expected, Displayed(ev, Tokyo).Origin);
        Assert.AreEqual(expected, Displayed(ev, NewYork).Origin, "別のタイムゾーンへ直しても出どころは残る");
    }

    [TestMethod]
    public void 手で作った予定は印の無い出どころ()
    {
        var ev = ServerEventMapper.ToDomain(ServerEvents.Single(42), new BusinessCalendarId(ServerDaysOffCalendar.Id));

        Assert.IsFalse(ev.IsReadOnly);
        Assert.AreEqual(EventOrigin.Manual, ev.Origin);
        Assert.AreEqual(EventOrigin.Manual, Displayed(ev, NewYork).Origin);
    }

    [TestMethod]
    public void 前のアプリのOutlookの取り込みはOutlookのまま()
    {
        Assert.AreEqual(EventOrigin.Outlook, new ExternalOrigin("outlook", "AAMkAD-1", null).Kind);
    }

    [TestMethod]
    public void 取り込みの出どころは手で作ったものにできない()
    {
        Assert.ThrowsExactly<DomainException>(() => new ExternalOrigin("x", "k", null, EventOrigin.Manual));
    }
}
