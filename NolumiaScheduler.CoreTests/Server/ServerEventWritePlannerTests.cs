using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.ValueObjects;
using NolumiaScheduler.Infrastructure.Server;

namespace NolumiaScheduler.CoreTests.Server;

[TestClass]
public class ServerEventWritePlannerTests
{
    private static readonly BusinessCalendarId DaysOff = new(ServerDaysOffCalendar.Id);
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);
    private static readonly OccurrenceLocalKey Nov4 = new(new LocalDateValue(2026, 11, 4), new LocalTimeValue(9, 0));

    private static CalendarEvent Read(EventDto dto) => ServerEventMapper.ToDomain(dto, DaysOff);

    [TestMethod]
    public void 何も変えていなければ送らない()
    {
        Assert.IsNull(ServerEventWritePlanner.NextStep(ServerEvents.Single(1), Read(ServerEvents.Single(1)), null));
        Assert.IsNull(ServerEventWritePlanner.NextStep(ServerEvents.Weekly(2), Read(ServerEvents.Weekly(2)), null));
    }

    [TestMethod]
    public void 新しい予定は作る_カレンダーは指定が無ければ既定()
    {
        var ev = CalendarEvent.CreateSingle(new EventId(Guid.NewGuid().ToString()), new EventTitle("New"), null,
            Visibility.Public, null, null, new TimeZoneId("Asia/Tokyo"),
            new SingleEventSchedule(new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero), 30), Now);

        var step = ServerEventWritePlanner.NextStep(null, ev, null);

        var create = (ServerEventStep.Create)step!;
        Assert.AreEqual("New", create.Body.Title);
        Assert.AreEqual("EVENT", create.Body.EventType);
        Assert.IsNull(create.Body.CalendarId);
        Assert.IsNull(create.Body.Recurrence);
        Assert.AreEqual(30, create.Body.DurationMinutes);
    }

    [TestMethod]
    public void 単発の題名を直すと_分類とタスクとカレンダーを送り返して直す()
    {
        var server = ServerEvents.Single(1);
        var ev = Read(server);
        ev.ChangeDetails(new EventTitle("Renamed"), ev.Location, ev.Visibility, ev.EventType, ev.Description, Now);

        var update = (ServerEventStep.UpdateSingle)ServerEventWritePlanner.NextStep(server, ev, null)!;

        Assert.AreEqual("Renamed", update.Body.Title);
        Assert.AreEqual(77L, update.Body.TaskId);
        Assert.AreEqual("TASK", update.Body.EventType);
        Assert.AreEqual(5L, update.Body.CalendarId);
        Assert.AreEqual(server.Version, update.Body.ExpectedVersion);
    }

    [TestMethod]
    public void 単発の時刻を動かすと直す()
    {
        var server = ServerEvents.Single(1);
        var ev = Read(server);
        ev.RescheduleSingle(new SingleEventSchedule(server.Start.AddHours(2), 90), Now);

        var update = (ServerEventStep.UpdateSingle)ServerEventWritePlanner.NextStep(server, ev, null)!;

        Assert.AreEqual(server.Start.AddHours(2), update.Body.Start);
        Assert.AreEqual(90, update.Body.DurationMinutes);
    }

    [TestMethod]
    public void 繰り返しの回を飛ばすと_飛ばす口を呼ぶ()
    {
        var server = ServerEvents.Weekly(2);
        var ev = Read(server);
        ev.SkipOccurrence(Nov4, Now);

        var skip = (ServerEventStep.Skip)ServerEventWritePlanner.NextStep(server, ev, null)!;

        Assert.AreEqual("2026-11-04", skip.Body.Occurrence.Date);
        Assert.AreEqual("09:00", skip.Body.Occurrence.StartTime);
        Assert.AreEqual(server.Version, skip.Body.ExpectedVersion);
    }

    [TestMethod]
    public void 飛ばした回を戻すと_戻す口を呼ぶ()
    {
        var server = ServerEvents.Weekly(2) with
        {
            Exceptions = [new EventExceptionDto(new OccurrenceKeyDto("2026-11-04", "09:00"), "SKIP")],
        };
        var ev = Read(server);
        ev.RemoveOccurrenceException(Nov4, Now);

        var restore = (ServerEventStep.Restore)ServerEventWritePlanner.NextStep(server, ev, null)!;

        Assert.AreEqual("2026-11-04", restore.Body.Occurrence.Date);
    }

    [TestMethod]
    public void 回を動かすと_予定のタイムゾーンでの瞬間を送る()
    {
        var server = ServerEvents.Weekly(2);
        var ev = Read(server);
        ev.MoveOccurrence(Nov4, new LocalDateValue(2026, 11, 5), new LocalTimeValue(14, 0), 45, null, null, null, Now);

        var move = (ServerEventStep.Move)ServerEventWritePlanner.NextStep(server, ev, null)!;

        Assert.AreEqual(new DateTimeOffset(2026, 11, 5, 5, 0, 0, TimeSpan.Zero), move.Body.Start, "14:00 Tokyo = 05:00Z");
        Assert.AreEqual(45, move.Body.DurationMinutes);
        Assert.AreEqual("2026-11-04", move.Body.Occurrence.Date);
    }

    [TestMethod]
    public void サーバーが返した移動と同じなら_もう送らない()
    {
        var server = ServerEvents.Weekly(2) with
        {
            Moves = [new EventMoveDto(new OccurrenceKeyDto("2026-11-04", "09:00"), "2026-11-05", "14:00", 45, null, null)],
        };
        var ev = Read(ServerEvents.Weekly(2));
        ev.MoveOccurrence(Nov4, new LocalDateValue(2026, 11, 5), new LocalTimeValue(14, 0), 45, null, null, null, Now);

        Assert.IsNull(ServerEventWritePlanner.NextStep(server, ev, null));
    }

    [TestMethod]
    public void 移動を取り消すと_取り消す口を呼ぶ()
    {
        var server = ServerEvents.Weekly(2) with
        {
            Moves = [new EventMoveDto(new OccurrenceKeyDto("2026-11-04", "09:00"), "2026-11-05", "14:00", 45, null, null)],
        };
        var ev = Read(server);
        ev.RemoveOccurrenceMove(Nov4, Now);

        var cancel = (ServerEventStep.CancelMove)ServerEventWritePlanner.NextStep(server, ev, null)!;

        Assert.AreEqual("2026-11-04", cancel.Body.Occurrence.Date);
    }

    [TestMethod]
    public void 繰り返しのすべてを直すと_系列を直す_回の操作より先()
    {
        var server = ServerEvents.Weekly(2) with { TaskId = 12, EventType = "TASK", CalendarId = 8 };
        var ev = Read(server);
        ev.ChangeDetails(new EventTitle("Daily"), null, ev.Visibility, ev.EventType, null, Now);
        ev.SkipOccurrence(Nov4, Now);

        var series = (ServerEventStep.UpdateSeries)ServerEventWritePlanner.NextStep(server, ev, null)!;

        Assert.AreEqual("Daily", series.Body.Title);
        Assert.AreEqual(12L, series.Body.TaskId);
        Assert.AreEqual("TASK", series.Body.EventType);
        Assert.AreEqual(8L, series.Body.CalendarId);
        Assert.AreEqual("WEEKLY", series.Body.Recurrence.Type);
    }

    [TestMethod]
    public void 以降を消す_終了日を変えると系列を直す()
    {
        var server = ServerEvents.Weekly(2);
        var ev = Read(server);
        ev.ChangeRecurrenceEndDate(new LocalDateValue(2026, 11, 30), Now);

        var series = (ServerEventStep.UpdateSeries)ServerEventWritePlanner.NextStep(server, ev, null)!;

        Assert.AreEqual("2026-11-30", series.Body.Recurrence.EndDate);
    }

    [TestMethod]
    public void 取り込んだ予定は書かない()
    {
        var imported = ServerEventMapper.ToDomain(
            new ImportedOccurrenceDto("x", 9, "Review", null, Now, 30, false), "Asia/Tokyo");

        Assert.ThrowsExactly<ArgumentException>(() => ServerEventWritePlanner.NextStep(null, imported, null));
    }
}
