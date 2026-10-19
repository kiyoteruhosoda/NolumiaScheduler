using System.Net;
using NolumiaScheduler.Infrastructure.ExternalCalendars;
using NolumiaScheduler.Infrastructure.Server;

namespace NolumiaScheduler.CoreTests.Server;

/// <summary>#537 AC1 (the app's half): the JSON Power Automate writes reaches the server unchanged.</summary>
[TestClass]
public class OutlookFeedSenderTests
{
    // The shape Power Automate's "Get calendar view of events (V3)" writes to OneDrive.
    private const string Feed = """
        {"value":[
          {"id":"AAMkAD-1","subject":"Weekly sync","start":"2026-11-05T05:00:00.0000000","end":"2026-11-05T05:30:00.0000000",
           "isAllDay":false,"location":"Teams","isReminderOn":true,"reminderMinutesBeforeStart":15,"sensitivity":"normal",
           "lastModifiedDateTime":"2026-10-19T08:00:00Z"},
          {"id":"AAMkAD-2","subject":"Clinic","start":"2026-11-06T00:00:00","end":"2026-11-06T01:00:00",
           "isAllDay":false,"sensitivity":"private"}
        ]}
        """;

    private string _dir = null!;
    private FakeSchedulerServer _server = null!;

    private string FeedFile => Path.Combine(_dir, "outlook-calendar.json");

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"nolumia-outlook-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _server = new FakeSchedulerServer()
            .On(HttpMethod.Post, "/api/calendars/imported", _ => (HttpStatusCode.Created, Calendar(31)))
            .On(HttpMethod.Put, "/api/calendars/31/outlook-feed", HttpStatusCode.OK, """{"source":"OUTLOOK_PUSH"}""");
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_dir, recursive: true);

    private static string Calendar(long id) => FakeSchedulerServer.Serialize(
        new CalendarDto(id, "IMPORTED", "Outlook", "DEFAULT", 3, false, null, null, false, "WORK"));

    private OutlookFeedSender Sender(FixedToken? token = null)
        => new(new SchedulerApiClient(new HttpClient(_server), new Uri("https://scheduler.example/"), token ?? new FixedToken()),
            _dir, FeedFile);

    private static OutlookSendOutcome Send(OutlookFeedSender sender) => sender.SendAsync(CancellationToken.None).GetAwaiter().GetResult();

    [TestMethod]
    public void 初めて送るとき_仕事のOutlookカレンダーを作り_置いたJSONをそのまま送り_idを覚える()
    {
        File.WriteAllText(FeedFile, Feed);

        Assert.AreEqual(OutlookSendOutcome.Sent, Send(Sender()));

        var created = _server.Sent(HttpMethod.Post, "/api/calendars/imported").Single().Json;
        Assert.AreEqual("Outlook", created.GetProperty("name").GetString());
        Assert.AreEqual("WORK", created.GetProperty("scope").GetString());
        Assert.AreEqual("OUTLOOK_PUSH", created.GetProperty("source").GetProperty("type").GetString());
        Assert.AreEqual(Feed, _server.Sent(HttpMethod.Put, "/api/calendars/31/outlook-feed").Single().Body, "中身は読み替えずに送る");
        Assert.AreEqual(31L, ExternalCalendarConfig.Load(_dir).ServerCalendarId);
    }

    [TestMethod]
    public void 覚えたカレンダーがあれば作らずに送る_同じ中身を何度送ってもよい()
    {
        File.WriteAllText(FeedFile, Feed);
        Send(Sender());

        var restarted = Sender();
        Send(restarted);
        Send(restarted);

        Assert.AreEqual(1, _server.Sent(HttpMethod.Post, "/api/calendars/imported").Count());
        Assert.AreEqual(3, _server.Sent(HttpMethod.Put, "/api/calendars/31/outlook-feed").Count());
    }

    [TestMethod]
    public void 覚えたカレンダーがサーバーで消されていたら_作り直して送る()
    {
        File.WriteAllText(FeedFile, Feed);
        ExternalCalendarConfig.SaveServerCalendarId(_dir, 8);
        _server.On(HttpMethod.Put, "/api/calendars/8/outlook-feed", HttpStatusCode.NotFound, """{"detail":"Not Found"}""");

        Assert.AreEqual(OutlookSendOutcome.Sent, Send(Sender()));

        Assert.AreEqual(1, _server.Sent(HttpMethod.Put, "/api/calendars/31/outlook-feed").Count());
        Assert.AreEqual(31L, ExternalCalendarConfig.Load(_dir).ServerCalendarId);
    }

    [TestMethod]
    public void 覚えたidを書いても_ほかの設定は残る()
    {
        File.WriteAllText(Path.Combine(_dir, ExternalCalendarConfig.FileName),
            """{"enabled":true,"filePath":"C:\\feeds\\o.json","intervalMinutes":7}""");

        ExternalCalendarConfig.SaveServerCalendarId(_dir, 31);

        var config = ExternalCalendarConfig.Load(_dir);
        Assert.AreEqual(@"C:\feeds\o.json", config.FilePath);
        Assert.AreEqual(7, config.IntervalMinutes);
        Assert.AreEqual(31L, config.ServerCalendarId);
    }

    [TestMethod]
    public void ファイルが無ければ何も送らない()
    {
        Assert.AreEqual(OutlookSendOutcome.NoFile, Send(Sender()));
        Assert.AreEqual(0, _server.Requests.Count);
    }

    [TestMethod]
    public void 書きかけのファイルは送らない()
    {
        File.WriteAllText(FeedFile, """{"value":[{"id":"AAMkAD-1","sub""");

        Assert.AreEqual(OutlookSendOutcome.Unreadable, Send(Sender()));
        Assert.AreEqual(0, _server.Requests.Count);
    }

    [TestMethod]
    public void ログインしていなければ送れない_次の周回でまた送る()
    {
        File.WriteAllText(FeedFile, Feed);

        Assert.ThrowsExactly<ServerNotSignedInException>(() => Send(Sender(new FixedToken(null))));
        Assert.IsNull(ExternalCalendarConfig.Load(_dir).ServerCalendarId);
    }
}
