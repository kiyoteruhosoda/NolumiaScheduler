using NolumiaScheduler.Infrastructure.Diagnostics;
using NolumiaScheduler.Infrastructure.ExternalCalendars;
using NolumiaScheduler.CoreTests.Server;
using NolumiaScheduler.Infrastructure.Server;

namespace NolumiaScheduler.CoreTests.Diagnostics;

[TestClass]
public class SyncStatusTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 0, 20, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Tokyo = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");

    private string _dir = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"nolumia-sync-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    // ── DiagnosticTime ───────────────────────────────────────────────────

    [TestMethod]
    public void Format_PCの時刻に時差を書き添える()
    {
        var moved = new DateTimeOffset(2026, 10, 20, 2, 14, 20, TimeSpan.Zero);
        Assert.AreEqual("2026/10/20 11:14:20 (UTC+09:00)", DiagnosticTime.Format(moved, Tokyo));
        Assert.AreEqual("2026/10/20 02:14:20 (UTC)", DiagnosticTime.Format(moved, TimeZoneInfo.Utc));
        Assert.AreEqual("UTC-05:30", DiagnosticTime.Offset(TimeSpan.FromMinutes(-330)));
    }

    // ── Diagnose ─────────────────────────────────────────────────────────

    private static SyncStatus Healthy() => new(
        Now, Tokyo, "scheduler.example", SignedIn: true, UserName: "kyon",
        MovedAt: Now.AddDays(-1), FetchedAt: Now.AddMinutes(-3),
        EventCount: 10, ImportedCount: 174, OutlookCount: 174, OutlookFirst: Now, OutlookLast: Now.AddDays(30),
        OutlookToday: [], Skipped: [],
        OutlookEnabled: true, OutlookFilePath: "x.json", OutlookIntervalMinutes: 15, OutlookCalendarId: 6,
        OutlookFileExists: true, OutlookFileWrittenAt: Now.AddMinutes(-10), OutlookFileSize: 1000,
        LastOutlookSend: Sent(Now.AddMinutes(-5)), LastOutlookSent: Sent(Now.AddMinutes(-5)),
        LastServerRead: Read(Now.AddMinutes(-3)), LastServerReadOk: Read(Now.AddMinutes(-3)),
        RecentLog: []);

    private static SyncAttempt Sent(DateTimeOffset at) => new(at, "timer", true, "Sent", null);
    private static SyncAttempt Read(DateTimeOffset at) => new(at, "timer", true, "Read", null);

    [TestMethod]
    public void Diagnose_送って読めていれば何も言わない()
        => CollectionAssert.AreEqual(Array.Empty<SyncProblem>(), Healthy().Problems.ToArray());

    [TestMethod]
    public void Diagnose_ログインしていない_移していない()
    {
        var s = Healthy() with { SignedIn = false, MovedAt = null };
        CollectionAssert.AreEqual(new[] { SyncProblem.NotSignedIn, SyncProblem.NotMovedToServer }, s.Problems.ToArray());
    }

    [TestMethod]
    public void Diagnose_送る設定が切れていればファイルは見ない()
    {
        var s = Healthy() with { OutlookEnabled = false, OutlookFileExists = false };
        CollectionAssert.AreEqual(new[] { SyncProblem.OutlookDisabled }, s.Problems.ToArray());
    }

    [TestMethod]
    public void Diagnose_ファイルが無い()
    {
        var s = Healthy() with { OutlookFileExists = false, OutlookFileWrittenAt = null };
        CollectionAssert.AreEqual(new[] { SyncProblem.OutlookFileMissing }, s.Problems.ToArray());
    }

    [TestMethod]
    public void Diagnose_ファイルが1日以上変わっていない()
    {
        var s = Healthy() with { OutlookFileWrittenAt = Now.AddHours(-25) };
        CollectionAssert.AreEqual(new[] { SyncProblem.OutlookFileOld }, s.Problems.ToArray());
    }

    [TestMethod]
    public void Diagnose_最後の送りが失敗()
    {
        var s = Healthy() with { LastOutlookSend = new SyncAttempt(Now.AddMinutes(-1), "timer", false, "Failed", "HTTP 413") };
        CollectionAssert.AreEqual(new[] { SyncProblem.OutlookSendFailed }, s.Problems.ToArray());
    }

    [TestMethod]
    public void Diagnose_変わったファイルを送っていない_少しは待つ()
    {
        var changed = Healthy() with { OutlookFileWrittenAt = Now.AddMinutes(-4) };
        var justNow = Healthy() with { OutlookFileWrittenAt = Now.AddMinutes(-1) };
        var neverSent = Healthy() with { LastOutlookSend = null, LastOutlookSent = null };

        CollectionAssert.AreEqual(new[] { SyncProblem.OutlookFileNotSentYet }, changed.Problems.ToArray());
        CollectionAssert.AreEqual(Array.Empty<SyncProblem>(), justNow.Problems.ToArray());
        CollectionAssert.AreEqual(new[] { SyncProblem.OutlookFileNotSentYet }, neverSent.Problems.ToArray());
    }

    [TestMethod]
    public void Diagnose_読めない_写しが古い_写しにOutlookが無い_読めない項目()
    {
        var s = Healthy() with
        {
            LastServerRead = new SyncAttempt(Now, "timer", false, "Failed", "server unreachable"),
            FetchedAt = Now.AddMinutes(-16),
            OutlookCount = 0,
            Skipped = ["imported x"],
        };
        CollectionAssert.AreEqual(
            new[] { SyncProblem.ServerReadFailed, SyncProblem.ServerCopyOld, SyncProblem.OutlookNotInCopy, SyncProblem.ItemsSkipped },
            s.Problems.ToArray());
    }

    [TestMethod]
    public void Diagnose_移す前はサーバーの写しを問わない()
    {
        var s = Healthy() with { MovedAt = null, FetchedAt = null, OutlookCount = 0 };
        CollectionAssert.AreEqual(new[] { SyncProblem.NotMovedToServer }, s.Problems.ToArray());
    }

    // ── Collect ──────────────────────────────────────────────────────────

    [TestMethod]
    public void Collect_ファイルの更新時刻と今日のOutlookの予定と送受の記録を集める()
    {
        var file = Path.Combine(_dir, "outlook-calendar.json");
        File.WriteAllText(file, "{}");
        var written = new DateTime(2026, 10, 21, 0, 15, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(file, written);

        var clock = new FakeClock(Now, Tokyo);
        var activity = new SyncActivity(clock);
        activity.OutlookSend("startup", true, "Sent");
        activity.OutlookSend("timer", false, "Failed", "HTTP 413");

        var status = SyncStatus.Collect(new SyncStatusSources(
            clock, "scheduler.example", true, "kyon", Now.AddDays(-1), Store: null,
            new ExternalCalendarConfig { Enabled = true, FilePath = file, IntervalMinutes = 15 },
            OutlookCalendarId: 6, activity, Path.Combine(_dir, "missing.log")));

        Assert.IsTrue(status.OutlookFileExists);
        Assert.AreEqual(new DateTimeOffset(written), status.OutlookFileWrittenAt);
        Assert.AreEqual(2L, status.OutlookFileSize);
        Assert.AreEqual("Sent", status.LastOutlookSent?.Result);
        Assert.AreEqual("HTTP 413", status.LastOutlookSend?.Error);
        Assert.AreEqual(0, status.RecentLog.Count);
        // No store (not moved): the copy is empty and never read.
        Assert.IsNull(status.FetchedAt);
        Assert.AreEqual(0, status.OutlookCount);
    }

    [TestMethod]
    public void Collect_写しのOutlookのカレンダーだけを数え_今日はPCの日付で選ぶ()
    {
        var server = new FakeSchedulerServer()
            .OnJson(HttpMethod.Get, "/api/calendars", Array.Empty<CalendarDto>())
            .OnJson(HttpMethod.Get, "/api/calendar/events", Array.Empty<EventDto>())
            .OnJson(HttpMethod.Get, "/api/calendars/days-off", Array.Empty<DayOffMarkDto>())
            .OnJson(HttpMethod.Get, "/api/calendars/imported-occurrences", new[]
            {
                // 10/21 08:00 JST (10/20 23:00 UTC): today in Tokyo.
                new ImportedOccurrenceDto("imported:6:0", 6, "タスク確認", null, new DateTimeOffset(2026, 10, 20, 23, 0, 0, TimeSpan.Zero), 30, false),
                new ImportedOccurrenceDto("imported:6:1", 6, "Skill1", null, new DateTimeOffset(2026, 10, 21, 1, 15, 0, TimeSpan.Zero), 30, false),
                // 10/22 09:00 JST: tomorrow.
                new ImportedOccurrenceDto("imported:6:2", 6, "Skill1", null, new DateTimeOffset(2026, 10, 22, 0, 0, 0, TimeSpan.Zero), 30, false),
                // Another imported calendar (.ics): not Outlook.
                new ImportedOccurrenceDto("imported:9:0", 9, "祝日", null, new DateTimeOffset(2026, 10, 21, 0, 0, 0, TimeSpan.Zero), 1440, true),
            });
        var clock = new FakeClock(Now, Tokyo);
        var store = new ServerCalendarStore(
            new SchedulerApiClient(new HttpClient(server), new Uri("https://scheduler.example/"), new FixedToken()),
            Path.Combine(_dir, "server-cache.json"), clock, "Asia/Tokyo");
        store.RefreshAsync(CancellationToken.None).GetAwaiter().GetResult();

        var status = SyncStatus.Collect(new SyncStatusSources(
            clock, "scheduler.example", true, null, Now.AddDays(-1), store,
            new ExternalCalendarConfig { Enabled = true, FilePath = Path.Combine(_dir, "none.json") },
            OutlookCalendarId: 6, new SyncActivity(clock), Path.Combine(_dir, "missing.log")));

        Assert.AreEqual(4, status.ImportedCount);
        Assert.AreEqual(3, status.OutlookCount);
        CollectionAssert.AreEqual(new[] { "タスク確認", "Skill1" }, status.OutlookToday.Select(o => o.Title).ToArray());
        Assert.AreEqual(Now, status.FetchedAt);
        Assert.IsFalse(status.OutlookFileExists);
        CollectionAssert.Contains(status.Problems.ToArray(), SyncProblem.OutlookFileMissing);
    }

    // ── AppLogTail ───────────────────────────────────────────────────────

    [TestMethod]
    public void AppLogTail_連携の行と警告以上だけ_例外は1行目だけ_新しい順に上限まで()
    {
        var log = Path.Combine(_dir, "nolumia-20261021.log");
        File.WriteAllLines(log,
        [
            "2026-10-21 09:00:00.000 +09:00 INFO   pid=1 tid=1 [Lifecycle] started",
            "2026-10-21 09:00:01.000 +09:00 INFO   pid=1 tid=1 [ExternalCalendar] Sending the Outlook feed: x every 15 min.",
            "2026-10-21 09:00:02.000 +09:00 WARNINGpid=1 tid=5 [ExternalCalendar] Outlook feed not sent (startup).",
            "    NolumiaScheduler.Infrastructure.Server.ServerNotSignedInException: Not signed in to the Scheduler server.",
            "       at Somewhere()",
            "2026-10-21 09:00:03.000 +09:00 WARNINGpid=1 tid=5 [Lifecycle] Could not read the Scheduler server (startup); showing the local copy.",
            "2026-10-21 09:00:04.000 +09:00 INFO   pid=1 tid=1 [Health] sample",
        ]);

        var all = AppLogTail.Read(log, [AppLogCategories.ExternalCalendar], 10);
        Assert.AreEqual(3, all.Count);
        StringAssert.Contains(all[1], "ServerNotSignedInException");
        Assert.IsFalse(all[1].Contains("at Somewhere()"));
        StringAssert.Contains(all[2], "[Lifecycle] Could not read");

        var last = AppLogTail.Read(log, [AppLogCategories.ExternalCalendar], 1);
        Assert.AreEqual(1, last.Count);
        StringAssert.Contains(last[0], "Could not read");

        Assert.AreEqual(0, AppLogTail.Read(Path.Combine(_dir, "none.log"), [], 10).Count);
    }

    // ── SyncActivity ─────────────────────────────────────────────────────

    [TestMethod]
    public void Describe_状態コードと理由を1行に()
    {
        Assert.AreEqual("not signed in", SyncActivity.Describe(new ServerNotSignedInException()));
        Assert.AreEqual("HTTP 413 feed_too_large: too big",
            SyncActivity.Describe(new ServerException("too big", System.Net.HttpStatusCode.RequestEntityTooLarge, "feed_too_large")));
        Assert.AreEqual("server unreachable: no route",
            SyncActivity.Describe(new ServerException("x", null, null, new HttpRequestException("no route"))));
        Assert.AreEqual("IOException: locked", SyncActivity.Describe(new IOException("locked")));
    }

    [TestMethod]
    public void SyncActivity_失敗しても最後に成功した回は残る()
    {
        var clock = new FakeClock(Now);
        var activity = new SyncActivity(clock);
        activity.ServerRead("startup", true);
        clock.Advance(TimeSpan.FromMinutes(5));
        activity.ServerRead("timer", false, "server unreachable: x");

        Assert.AreEqual(Now, activity.LastServerReadOk?.At);
        Assert.AreEqual(false, activity.LastServerRead?.Succeeded);
        Assert.AreEqual(Now.AddMinutes(5), activity.LastServerRead?.At);
    }
}
