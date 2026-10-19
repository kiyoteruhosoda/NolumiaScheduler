using System.Net;
using NolumiaScheduler.Application.Commands;
using NolumiaScheduler.Application.Services;
using NolumiaScheduler.Domain.Exceptions;
using NolumiaScheduler.Domain.ValueObjects;
using NolumiaScheduler.Infrastructure.Server;

namespace NolumiaScheduler.CoreTests.Server;

[TestClass]
public class ServerCalendarStoreTests
{
    private string _dir = null!;
    private FakeSchedulerServer _server = null!;
    private FakeClock _clock = null!;

    private string CacheFile => Path.Combine(_dir, "server-cache.json");

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"nolumia-server-{Guid.NewGuid():N}");
        _clock = new FakeClock(new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero));
        _server = new FakeSchedulerServer()
            .OnJson(HttpMethod.Get, "/api/calendars", new[]
            {
                new CalendarDto(5, "EVENTS", "予定", "DEFAULT", 0, true, null, null, false, "WORK"),
                new CalendarDto(1, "WORKWEEK", "営業日", "DEFAULT", 1, false, ["MO", "TU", "WE", "TH", "FR"], null, false, "WORK"),
            })
            .OnJson(HttpMethod.Get, "/api/calendar/events", new[] { ServerEvents.Single(1), ServerEvents.Weekly(2) })
            .OnJson(HttpMethod.Get, "/api/calendars/days-off", new[]
            {
                new DayOffMarkDto("2026-11-03", "NATIONAL_HOLIDAY", 2, "文化の日", true),
            })
            .OnJson(HttpMethod.Get, "/api/calendars/imported-occurrences", new[]
            {
                new ImportedOccurrenceDto("imported:9:0", 9, "Outlook meeting", "Teams",
                    new DateTimeOffset(2026, 11, 5, 5, 0, 0, TimeSpan.Zero), 30, false,
                    Reminder: new AlarmDto(true, true, false, false, true)),
            });
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private ServerCalendarStore NewStore(FixedToken? token = null)
        => new(new SchedulerApiClient(new HttpClient(_server), new Uri("https://scheduler.example/"), token ?? new FixedToken()),
            CacheFile, _clock, "Asia/Tokyo");

    private static void Refresh(ServerCalendarStore store) => store.RefreshAsync(CancellationToken.None).GetAwaiter().GetResult();

    // ── Reading ──────────────────────────────────────────────────────────

    [TestMethod]
    public void Refresh_予定と取り込んだ回と休みの日が読める_Bearerで呼ぶ()
    {
        var store = NewStore();
        var repo = new ServerCalendarEventRepository(store);

        Refresh(store);

        var all = repo.FindAll();
        Assert.AreEqual(3, all.Count);
        Assert.AreEqual("Meeting", repo.FindById(new EventId("1"))!.Title.Value);
        var outlook = all.Single(e => e.IsReadOnly);
        Assert.AreEqual("Outlook meeting", outlook.Title.Value);
        Assert.IsTrue(outlook.Alarm!.Notify15Min);
        Assert.IsTrue(store.DaysOff.IsHoliday(new LocalDateValue(2026, 11, 3)));
        Assert.IsTrue(_server.Requests.All(r => r.Authorization == "Bearer token-1"));
    }

    [TestMethod]
    public void Refresh_休みと取り込んだ回は800日以内で聞く()
    {
        var store = NewStore();

        Refresh(store);

        foreach (var request in _server.Requests.Where(r => r.PathAndQuery.Contains("days-off") || r.PathAndQuery.Contains("imported")))
        {
            var query = System.Web.HttpUtility.ParseQueryString(new Uri("https://x" + request.PathAndQuery).Query);
            var days = DateOnly.Parse(query["to"]!).DayNumber - DateOnly.Parse(query["from"]!).DayNumber + 1;
            Assert.IsTrue(days <= 800, request.PathAndQuery);
        }
    }

    [TestMethod]
    public void 写しはファイルに残り_次の起動はつながらなくても表示できる()
    {
        Refresh(NewStore());
        _server.Offline = true;

        var restarted = new ServerCalendarEventRepository(NewStore());

        Assert.AreEqual(3, restarted.FindAll().Count);
        Assert.AreEqual("Standup", restarted.FindById(new EventId("2"))!.Title.Value);
    }

    [TestMethod]
    public void つながらないときの読み直しは失敗し_写しはそのまま()
    {
        var store = NewStore();
        Refresh(store);
        _server.Offline = true;

        var error = Assert.ThrowsExactly<ServerException>(() => Refresh(store));

        Assert.IsTrue(error.IsUnreachable);
        Assert.AreEqual(3, new ServerCalendarEventRepository(store).FindAll().Count);
    }

    [TestMethod]
    public void 何も変わらない読み直しでは変更を知らせない()
    {
        var store = NewStore();
        Refresh(store);
        var raised = 0;
        store.Changed += () => raised++;

        Refresh(store);

        Assert.AreEqual(0, raised, "知らせるとアラームの鳴った印が消えてしまう");
    }

    // ── Writing through the app's own service ───────────────────────────

    private CalendarEventApplicationService Service(ServerCalendarStore store)
    {
        var repo = new ServerCalendarEventRepository(store);
        return new CalendarEventApplicationService(repo, repo, _clock);
    }

    private static UpdateEventCommand Rename(string id, string title)
        => new(id, title, "Room A", Visibility.Public, false, null, null, null, EventAlarm.Default);

    [TestMethod]
    public void 予定を直すと_知らない項目を送り返して直し_写しはサーバーの答えになる()
    {
        _server.OnJson(HttpMethod.Put, "/api/calendar/events/1", ServerEvents.Single(1, "Renamed", version: 4));
        var store = NewStore();
        Refresh(store);

        Service(store).UpdateEvent(Rename("1", "Renamed"));

        var put = _server.Sent(HttpMethod.Put, "/api/calendar/events/1").Single().Json;
        Assert.AreEqual("Renamed", put.GetProperty("title").GetString());
        Assert.AreEqual(77, put.GetProperty("task_id").GetInt64());
        Assert.AreEqual("TASK", put.GetProperty("event_type").GetString());
        Assert.AreEqual(5, put.GetProperty("calendar_id").GetInt64());
        Assert.AreEqual(3, put.GetProperty("expected_version").GetInt32());
        Assert.AreEqual("Renamed", store.FindDto("1")!.Title);
        Assert.AreEqual(4, store.FindDto("1")!.Version);
    }

    [TestMethod]
    public void 版がぶつかったら_読み直してから知らせる()
    {
        _server.On(HttpMethod.Put, "/api/calendar/events/1", HttpStatusCode.Conflict, """{"detail":"version mismatch"}""");
        var store = NewStore();
        Refresh(store);
        _server.OnJson(HttpMethod.Get, "/api/calendar/events", new[] { ServerEvents.Single(1, "Changed elsewhere", version: 9), ServerEvents.Weekly(2) });

        var error = Assert.ThrowsExactly<ServerException>(() => Service(store).UpdateEvent(Rename("1", "Mine")));

        Assert.IsTrue(error.IsConflict);
        Assert.AreEqual("Changed elsewhere", store.FindDto("1")!.Title);
        Assert.AreEqual(9, store.FindDto("1")!.Version);
    }

    [TestMethod]
    public void つながらないときの書き込みは失敗し_画面は元に戻る()
    {
        var store = NewStore();
        Refresh(store);
        var repo = new ServerCalendarEventRepository(store);
        var raised = 0;
        repo.Changed += () => raised++;
        _server.Offline = true;

        var error = Assert.ThrowsExactly<ServerException>(() => Service(store).UpdateEvent(Rename("1", "Offline edit")));

        Assert.IsTrue(error.IsUnreachable);
        Assert.AreEqual("Meeting", repo.FindById(new EventId("1"))!.Title.Value, "手元に溜めない");
        Assert.IsTrue(raised > 0, "画面を描き直させる");
    }

    [TestMethod]
    public void 新しい予定はサーバーが振った番号に置き換わる()
    {
        _server.On(HttpMethod.Post, "/api/calendar/events",
            _ => (HttpStatusCode.Created, FakeSchedulerServer.Serialize(ServerEvents.Single(100, "Lunch", version: 1) with { TaskId = null, EventType = "EVENT" })));
        var store = NewStore();
        Refresh(store);

        Service(store).CreateSingleEvent(new CreateSingleEventCommand(
            "Lunch", "Room A", Visibility.Public, null, null, "Asia/Tokyo", false,
            new DateOnly(2026, 11, 2), new TimeSpan(10, 0, 0), new TimeSpan(11, 0, 0), EventAlarm.Default));

        var post = _server.Sent(HttpMethod.Post, "/api/calendar/events").Single().Json;
        Assert.AreEqual("Lunch", post.GetProperty("title").GetString());
        Assert.AreEqual("2026-11-02T01:00:00+00:00", post.GetProperty("start").GetString());
        var ids = new ServerCalendarEventRepository(store).FindAll().Select(e => e.Id.Value).ToList();
        CollectionAssert.Contains(ids, "100");
        Assert.IsFalse(ids.Any(id => Guid.TryParse(id, out _)));
    }

    [TestMethod]
    public void 繰り返しの回を消すと_飛ばす口を呼ぶ()
    {
        var skipped = ServerEvents.Weekly(2, version: 5) with
        {
            Exceptions = [new EventExceptionDto(new OccurrenceKeyDto("2026-11-04", "09:00"), "SKIP")],
        };
        _server.OnJson(HttpMethod.Post, "/api/calendar/events/2/occurrences/skip", skipped);
        var store = NewStore();
        Refresh(store);

        Service(store).DeleteOccurrence(new SkipOccurrenceCommand("2",
            new OccurrenceLocalKey(new LocalDateValue(2026, 11, 4), new LocalTimeValue(9, 0))));

        var body = _server.Sent(HttpMethod.Post, "/api/calendar/events/2/occurrences/skip").Single().Json;
        Assert.AreEqual("2026-11-04", body.GetProperty("occurrence").GetProperty("date").GetString());
        Assert.AreEqual(4, body.GetProperty("expected_version").GetInt32());
        Assert.AreEqual(1, store.FindDto("2")!.Exceptions.Count);
        Assert.AreEqual(1, _server.Requests.Count(r => r.Method != HttpMethod.Get), "収まったらそれ以上送らない");
    }

    [TestMethod]
    public void 予定を消すとサーバーから消え_写しからも消える()
    {
        _server.On(HttpMethod.Delete, "/api/calendar/events/1", HttpStatusCode.NoContent, "");
        var store = NewStore();
        Refresh(store);

        Service(store).DeleteEvent("1");

        Assert.AreEqual(1, _server.Sent(HttpMethod.Delete, "/api/calendar/events/1").Count());
        Assert.IsNull(store.FindDto("1"));
    }

    [TestMethod]
    public void 取り込んだ回は消せず_通知の切り替えはサーバーへ送らない()
    {
        var store = NewStore();
        Refresh(store);
        var service = Service(store);
        var outlook = new ServerCalendarEventRepository(store).FindAll().Single(e => e.IsReadOnly);

        Assert.ThrowsExactly<DomainException>(() => service.DeleteEvent(outlook.Id.Value));
        service.SetEventAlarm(outlook.Id.Value, new EventAlarm(false, false, false, false, false));

        Assert.IsFalse(_server.Requests.Any(r => r.Method != HttpMethod.Get));
        Assert.IsFalse(new ServerCalendarEventRepository(store).FindById(outlook.Id)!.Alarm!.IsEnabled);
    }

    [TestMethod]
    public void ログインしていなければ書けない()
    {
        var store = NewStore(new FixedToken(null));

        var error = Assert.ThrowsExactly<ServerNotSignedInException>(() => Refresh(store));

        Assert.AreEqual(HttpStatusCode.Unauthorized, error.StatusCode);
    }

    [TestMethod]
    public void 営業日はサーバーの休みの層の1つだけで_画面から直せない()
    {
        var store = NewStore();
        Refresh(store);
        var calendars = new ServerBusinessCalendarRepository(store);

        Assert.AreEqual(1, calendars.FindAll().Count);
        Assert.IsTrue(calendars.FindById(new BusinessCalendarId(ServerDaysOffCalendar.Id))!.IsHoliday(new LocalDateValue(2026, 11, 3)));
        Assert.ThrowsExactly<DomainException>(() => calendars.Save(calendars.FindAll()[0]));
    }
}
