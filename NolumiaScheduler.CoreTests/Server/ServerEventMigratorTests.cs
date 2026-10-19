using System.Net;
using System.Text.Json;
using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.ValueObjects;
using NolumiaScheduler.Infrastructure.Json.Repositories;
using NolumiaScheduler.Infrastructure.Server;
using Location = NolumiaScheduler.Domain.ValueObjects.Location;

namespace NolumiaScheduler.CoreTests.Server;

/// <summary>#537 AC2: the tool that moves the PC's events to the server.</summary>
[TestClass]
public class ServerEventMigratorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneId Tokyo = new("Asia/Tokyo");

    private string _dir = null!;
    private FakeSchedulerServer _server = null!;
    private readonly Dictionary<string, long> _serverRefs = [];
    private readonly List<CalendarDto> _calendars = [];
    private int _failImportCall;
    private int _importCalls;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"nolumia-migrator-{Guid.NewGuid():N}");
        _calendars.Add(new CalendarDto(5, "EVENTS", "予定", "DEFAULT", 0, true, null, null, false, "WORK"));
        _server = new FakeSchedulerServer()
            .On(HttpMethod.Get, "/api/calendars", _ => (HttpStatusCode.OK, FakeSchedulerServer.Serialize(_calendars.ToArray())))
            .On(HttpMethod.Post, "/api/calendars", request =>
            {
                var created = new CalendarDto(40 + _calendars.Count, "EVENTS", request.Json.GetProperty("name").GetString()!,
                    "DEFAULT", _calendars.Count, false, null, null, false, request.Json.GetProperty("scope").GetString()!);
                _calendars.Add(created);
                return (HttpStatusCode.Created, FakeSchedulerServer.Serialize(created));
            })
            .On(HttpMethod.Post, "/api/calendar/events/import", Import);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    // The server's contract: a client_ref it has seen is not created again; its id is returned.
    private (HttpStatusCode, string) Import(ReceivedRequest request)
    {
        _importCalls++;
        if (_importCalls == _failImportCall)
            return (HttpStatusCode.InternalServerError, """{"detail":"boom"}""");
        var results = new List<EventImportResultDto>();
        foreach (var item in request.Json.GetProperty("events").EnumerateArray())
        {
            var clientRef = item.GetProperty("client_ref").GetString()!;
            var created = !_serverRefs.ContainsKey(clientRef);
            if (created) _serverRefs[clientRef] = 1000 + _serverRefs.Count;
            results.Add(new EventImportResultDto(clientRef, _serverRefs[clientRef], created));
        }
        return (HttpStatusCode.OK, FakeSchedulerServer.Serialize(new EventImportResponseDto(results)));
    }

    private ServerEventMigrator Migrator(int batchSize = 50)
        => new(new SchedulerApiClient(new HttpClient(_server), new Uri("https://scheduler.example/"), new FixedToken()),
            new FakeClock(Now), batchSize);

    private JsonCalendarEventRepository LocalStore() => new(Path.Combine(_dir, "events"));

    private ServerMigrationReport Run(ServerEventMigrator migrator, JsonCalendarEventRepository store)
        => migrator.MigrateAsync(store, _dir, CancellationToken.None).GetAwaiter().GetResult();

    private IEnumerable<JsonElement> SentItems()
        => _server.Sent(HttpMethod.Post, "/api/calendar/events/import").SelectMany(r => r.Json.GetProperty("events").EnumerateArray());

    private static CalendarEvent Single(string title, Visibility visibility = Visibility.Public) => CalendarEvent.CreateSingle(
        new EventId(Guid.NewGuid().ToString()), new EventTitle(title), new Location("Room A"), visibility,
        new EventType("Meeting"), new Description("memo"), Tokyo,
        new SingleEventSchedule(new DateTimeOffset(2026, 11, 2, 1, 0, 0, TimeSpan.Zero), 60), Now,
        alarm: new EventAlarm(true, false, true, false, true), colorKey: EventColorKey.Basil);

    private static CalendarEvent MonthEndReport()
    {
        var rule = new RecurrenceRule(RecurrenceType.Monthly, 1, new LocalDateValue(2027, 3, 31),
            monthly: new LastDayOfMonthMonthlyRule(),
            adjustment: new AdjustmentRule(AdjustmentCondition.Holiday, AdjustmentShiftUnit.BusinessDay, -1,
                new BusinessCalendarId("jp-business-calendar")));
        var ev = CalendarEvent.CreateRecurring(
            new EventId(Guid.NewGuid().ToString()), new EventTitle("Month-end report"), null, Visibility.Public,
            null, null, Tokyo, new RecurringEventSchedule(new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.Zero), 30, rule), Now);
        var nov = new OccurrenceLocalKey(new LocalDateValue(2026, 11, 30), new LocalTimeValue(9, 0));
        var dec = new OccurrenceLocalKey(new LocalDateValue(2026, 12, 31), new LocalTimeValue(9, 0));
        ev.SkipOccurrence(nov, Now);
        ev.MoveOccurrence(dec, new LocalDateValue(2026, 12, 28), new LocalTimeValue(15, 0), 45, null, null, null, Now);
        return ev;
    }

    private static CalendarEvent FromOutlook() => CalendarEvent.CreateExternalSingle(
        new EventId(Guid.NewGuid().ToString()), new ExternalOrigin("outlook", "AAMkAD-1", Now),
        new EventTitle("Outlook meeting"), null, Visibility.Public, Tokyo,
        new SingleEventSchedule(new DateTimeOffset(2026, 11, 3, 1, 0, 0, TimeSpan.Zero), 30), null, Now);

    // ── The mapping table ────────────────────────────────────────────────

    [TestMethod]
    public void 単発の予定は表のとおりに写り_手元のidがclient_refになる()
    {
        var store = LocalStore();
        var ev = Single("Design review");
        store.Save(ev);

        Run(Migrator(), store);

        var item = SentItems().Single();
        Assert.AreEqual(ev.Id.Value, item.GetProperty("client_ref").GetString());
        Assert.AreEqual("Design review", item.GetProperty("title").GetString());
        Assert.AreEqual("Room A", item.GetProperty("location").GetString());
        Assert.AreEqual("memo", item.GetProperty("description").GetString());
        Assert.AreEqual("Asia/Tokyo", item.GetProperty("time_zone").GetString());
        Assert.AreEqual(DateTimeOffset.Parse("2026-11-02T01:00:00Z"), item.GetProperty("start").GetDateTimeOffset());
        Assert.AreEqual(60, item.GetProperty("duration_minutes").GetInt32());
        Assert.AreEqual("BASIL", item.GetProperty("color_key").GetString());
        var alarm = item.GetProperty("alarm");
        Assert.IsTrue(alarm.GetProperty("enabled").GetBoolean());
        Assert.IsFalse(alarm.GetProperty("notify_15_min").GetBoolean());
        Assert.IsTrue(alarm.GetProperty("notify_5_min").GetBoolean());
        Assert.AreEqual(JsonValueKind.Null, item.GetProperty("recurrence").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, item.GetProperty("calendar_id").ValueKind, "公開は既定（仕事）のカレンダー");
        Assert.AreEqual("EVENT", item.GetProperty("event_type").GetString(), "予定の種類（Meeting）は持っていかない");
        Assert.IsFalse(item.TryGetProperty("visibility", out _));
        Assert.IsFalse(item.TryGetProperty("version", out _));
    }

    [TestMethod]
    public void 繰り返しは規則と飛ばした回と動かした回が写り_営業日シフトは名指しを外してシフトだけ残す()
    {
        var store = LocalStore();
        store.Save(MonthEndReport());

        Run(Migrator(), store);

        var item = SentItems().Single();
        var rule = item.GetProperty("recurrence");
        Assert.AreEqual("MONTHLY", rule.GetProperty("type").GetString());
        Assert.AreEqual("LAST_DAY", rule.GetProperty("monthly").GetProperty("kind").GetString());
        Assert.AreEqual("2027-03-31", rule.GetProperty("end_date").GetString());
        var adjustment = rule.GetProperty("adjustment");
        Assert.AreEqual("HOLIDAY", adjustment.GetProperty("condition").GetString());
        Assert.AreEqual("BUSINESS_DAY", adjustment.GetProperty("shift_unit").GetString());
        Assert.AreEqual(-1, adjustment.GetProperty("shift_amount").GetInt32());
        Assert.IsFalse(adjustment.TryGetProperty("calendar_id", out _), "営業日カレンダーの名指しは外す");

        var skip = item.GetProperty("exceptions").EnumerateArray().Single();
        Assert.AreEqual("SKIP", skip.GetProperty("type").GetString());
        Assert.AreEqual("2026-11-30", skip.GetProperty("occurrence").GetProperty("date").GetString());
        Assert.AreEqual("09:00", skip.GetProperty("occurrence").GetProperty("start_time").GetString());

        var move = item.GetProperty("moves").EnumerateArray().Single();
        Assert.AreEqual("2026-12-31", move.GetProperty("occurrence").GetProperty("date").GetString());
        Assert.AreEqual("2026-12-28", move.GetProperty("new_date").GetString());
        Assert.AreEqual("15:00", move.GetProperty("new_start_time").GetString());
        Assert.AreEqual(45, move.GetProperty("new_duration_minutes").GetInt32());
    }

    [TestMethod]
    public void 公開範囲が非公開と機密の予定はプライベートのカレンダーへ_無ければ作る()
    {
        var store = LocalStore();
        var secret = Single("Clinic", Visibility.Private);
        var confidential = Single("Interview", Visibility.Confidential);
        var open = Single("Standup");
        store.Save(secret);
        store.Save(confidential);
        store.Save(open);

        var report = Run(Migrator(), store);

        var created = _server.Sent(HttpMethod.Post, "/api/calendars").Single().Json;
        Assert.AreEqual(ServerEventMigrator.PrivateCalendarName, created.GetProperty("name").GetString());
        Assert.AreEqual("PRIVATE", created.GetProperty("scope").GetString());
        var privateId = _calendars.Single(c => c.Scope == "PRIVATE").Id;
        var byRef = SentItems().ToDictionary(i => i.GetProperty("client_ref").GetString()!);
        Assert.AreEqual(privateId, byRef[secret.Id.Value].GetProperty("calendar_id").GetInt64());
        Assert.AreEqual(privateId, byRef[confidential.Id.Value].GetProperty("calendar_id").GetInt64());
        Assert.AreEqual(JsonValueKind.Null, byRef[open.Id.Value].GetProperty("calendar_id").ValueKind);
        Assert.AreEqual(2, report.SentToPrivate);
    }

    [TestMethod]
    public void プライベートのカレンダーがもうあればそれを使う()
    {
        _calendars.Add(new CalendarDto(9, "EVENTS", "私用", "DEFAULT", 1, false, null, null, false, "PRIVATE"));
        var store = LocalStore();
        store.Save(Single("Clinic", Visibility.Private));

        Run(Migrator(), store);

        Assert.AreEqual(0, _server.Sent(HttpMethod.Post, "/api/calendars").Count());
        Assert.AreEqual(9, SentItems().Single().GetProperty("calendar_id").GetInt64());
    }

    [TestMethod]
    public void Outlookから取り込んだ予定は移さない()
    {
        var store = LocalStore();
        store.Save(Single("Mine"));
        store.Save(FromOutlook());

        var report = Run(Migrator(), store);

        Assert.AreEqual("Mine", SentItems().Single().GetProperty("title").GetString());
        Assert.AreEqual(1, report.SkippedImported);
        Assert.AreEqual(1, report.Sent);
    }

    [TestMethod]
    public void Windowsのタイムゾーン名はIANAの名前にして送る()
    {
        var store = LocalStore();
        var ev = CalendarEvent.CreateSingle(new EventId(Guid.NewGuid().ToString()), new EventTitle("Old"), null,
            Visibility.Public, null, null, new TimeZoneId("Tokyo Standard Time"),
            new SingleEventSchedule(new DateTimeOffset(2026, 11, 2, 1, 0, 0, TimeSpan.Zero), 60), Now);
        store.Save(ev);

        Run(Migrator(), store);

        Assert.AreEqual("Asia/Tokyo", SentItems().Single().GetProperty("time_zone").GetString());
    }

    // ── Running again ────────────────────────────────────────────────────

    [TestMethod]
    public void もう一度流しても二重にならない()
    {
        var store = LocalStore();
        for (var i = 0; i < 5; i++) store.Save(Single($"Event {i}"));

        var first = Run(Migrator(), store);
        var second = Run(Migrator(), store);

        Assert.AreEqual(5, first.Created);
        Assert.AreEqual(0, second.Created);
        Assert.AreEqual(5, second.AlreadyOnServer);
        Assert.AreEqual(5, _serverRefs.Count, "サーバーにある予定は 5 件のまま");
    }

    [TestMethod]
    public void 途中で落ちても_やり直せば続きから入り二重にならない()
    {
        var store = LocalStore();
        for (var i = 0; i < 5; i++) store.Save(Single($"Event {i}"));
        _failImportCall = 2; // the second batch of two fails

        Assert.ThrowsExactly<ServerException>(() => Run(Migrator(batchSize: 2), store));
        Assert.IsFalse(ServerEventMigrator.IsMigrated(_dir), "全部入るまでは「移した」印を置かない");
        Assert.AreEqual(2, _serverRefs.Count);

        var report = Run(Migrator(batchSize: 2), store);

        Assert.AreEqual(5, _serverRefs.Count);
        Assert.AreEqual(3, report.Created);
        Assert.AreEqual(2, report.AlreadyOnServer);
    }

    [TestMethod]
    public void 移し終えたら印を置き_手元の予定のファイルは消さない()
    {
        var store = LocalStore();
        store.Save(Single("Keep me"));
        var filesBefore = Directory.GetFiles(Path.Combine(_dir, "events"));

        Run(Migrator(), store);

        var marker = ServerEventMigrator.ReadMarker(_dir)!;
        Assert.AreEqual(1, marker.Sent);
        Assert.AreEqual(1, marker.Created);
        Assert.AreEqual(Now, marker.MigratedAt);
        Assert.AreEqual("https://scheduler.example/", marker.Server);
        CollectionAssert.AreEquivalent(filesBefore, Directory.GetFiles(Path.Combine(_dir, "events")));
        Assert.AreEqual(1, LocalStore().FindAll().Count);
    }

    [TestMethod]
    public void 一度に送るのは100件まで()
    {
        var store = LocalStore();
        for (var i = 0; i < 3; i++) store.Save(Single($"Event {i}"));

        Run(Migrator(batchSize: 500), store);

        Assert.IsTrue(_server.Sent(HttpMethod.Post, "/api/calendar/events/import")
            .All(r => r.Json.GetProperty("events").GetArrayLength() <= ServerEventMigrator.MaxBatchSize));
    }
}
