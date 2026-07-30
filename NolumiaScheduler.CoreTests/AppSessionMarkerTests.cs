using NolumiaScheduler.Infrastructure.Diagnostics;

namespace NolumiaScheduler.CoreTests;

[TestClass]
public class AppSessionMarkerTests
{
    private string _dir = null!;

    [TestInitialize]
    public void Setup()
        => _dir = Path.Combine(Path.GetTempPath(), $"nolumia-session-{Guid.NewGuid():N}");

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private static FakeClock ClockAt(int hour, int minute = 0)
        => new(new DateTimeOffset(2026, 7, 30, hour, minute, 0, TimeSpan.Zero));

    [TestMethod]
    public void FirstRun_HasNoPreviousSession()
    {
        var marker = new AppSessionMarker(_dir, ClockAt(9), "v1");

        Assert.IsNull(marker.PreviousSession);
        Assert.IsFalse(marker.PreviousSessionCrashed);
    }

    [TestMethod]
    public void CleanExit_IsReportedAsCleanOnTheNextRun()
    {
        new AppSessionMarker(_dir, ClockAt(9), "v1").MarkCleanExit("tray exit");

        var next = new AppSessionMarker(_dir, ClockAt(10), "v1");

        Assert.IsNotNull(next.PreviousSession);
        Assert.IsTrue(next.PreviousSession!.CleanExit);
        Assert.IsFalse(next.PreviousSessionCrashed);
        Assert.AreEqual("tray exit", next.PreviousSession.ExitReason);
    }

    [TestMethod]
    public void SessionThatNeverExits_IsReportedAsCrashedOnTheNextRun()
    {
        // No MarkCleanExit call at all: the process simply vanished, which is exactly the case
        // that leaves no exception and no event-log entry behind.
        var clock = ClockAt(9);
        var marker = new AppSessionMarker(_dir, clock, "v1");
        clock.Advance(TimeSpan.FromMinutes(30));
        marker.Heartbeat();

        var next = new AppSessionMarker(_dir, ClockAt(11), "v1");

        Assert.IsTrue(next.PreviousSessionCrashed);
        Assert.AreEqual(TimeSpan.FromMinutes(30), next.PreviousSession!.Uptime);
    }

    [TestMethod]
    public void RecordEvent_PreservesTheLastMachineStateForTheNextRun()
    {
        var marker = new AppSessionMarker(_dir, ClockAt(9), "v1");
        marker.RecordEvent("suspend");
        marker.RecordEvent("resume");

        var next = new AppSessionMarker(_dir, ClockAt(10), "v1");

        // This is the link between "the app died" and "we had just resumed".
        Assert.AreEqual("resume", next.PreviousSession!.LastEvent);
        Assert.IsTrue(next.PreviousSessionCrashed);
    }

    [TestMethod]
    public void Heartbeat_DoesNotOverwriteTheLastRecordedEvent()
    {
        var clock = ClockAt(9);
        var marker = new AppSessionMarker(_dir, clock, "v1");
        marker.RecordEvent("resume");

        clock.Advance(TimeSpan.FromMinutes(5));
        marker.Heartbeat();

        var next = new AppSessionMarker(_dir, ClockAt(11), "v1");
        Assert.AreEqual("resume", next.PreviousSession!.LastEvent);
        Assert.AreEqual(TimeSpan.FromMinutes(5), next.PreviousSession.Uptime);
    }

    [TestMethod]
    public void MarkCrashed_CarriesTheReasonForward()
    {
        new AppSessionMarker(_dir, ClockAt(9), "v1")
            .MarkCrashed("unhandled-exception: InvalidOperationException");

        var next = new AppSessionMarker(_dir, ClockAt(10), "v1");

        Assert.IsTrue(next.PreviousSessionCrashed);
        StringAssert.Contains(next.PreviousSession!.ExitReason, "InvalidOperationException");
    }

    [TestMethod]
    public void CleanExitAfterACrash_DoesNotEraseTheCrash()
    {
        // The fatal-error path reports the crash and then shuts the app down in an orderly way,
        // so the shutdown must not be able to overwrite the record it follows.
        var marker = new AppSessionMarker(_dir, ClockAt(9), "v1");
        marker.MarkCrashed("unhandled-exception: XamlParseException");
        marker.MarkCleanExit("message loop ended");

        var next = new AppSessionMarker(_dir, ClockAt(10), "v1");

        Assert.IsTrue(next.PreviousSessionCrashed);
        StringAssert.Contains(next.PreviousSession!.ExitReason, "XamlParseException");
    }

    [TestMethod]
    public void MarkCrashed_KeepsTheFirstReason()
    {
        var marker = new AppSessionMarker(_dir, ClockAt(9), "v1");
        marker.MarkCrashed("first fault");
        marker.MarkCrashed("secondary fallout");

        var next = new AppSessionMarker(_dir, ClockAt(10), "v1");

        Assert.AreEqual("first fault", next.PreviousSession!.ExitReason);
    }

    [TestMethod]
    public void PreviousSession_RecordsTheBuildIdentity()
    {
        new AppSessionMarker(_dir, ClockAt(9), "v1.2.3-4-gabcdef").MarkCleanExit("tray exit");

        var next = new AppSessionMarker(_dir, ClockAt(10), "v1.2.4");

        // A crash that only happens on one build is only visible if the marker names the build.
        Assert.AreEqual("v1.2.3-4-gabcdef", next.PreviousSession!.AppVersion);
    }

    [TestMethod]
    public void CorruptMarker_IsTreatedAsNoInformation()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "session.txt"), "this is not a marker");

        var marker = new AppSessionMarker(_dir, ClockAt(9), "v1");

        // Reporting a false crash would be worse than reporting nothing.
        Assert.IsNull(marker.PreviousSession);
        Assert.IsFalse(marker.PreviousSessionCrashed);
    }

    [TestMethod]
    public void MultiLineValues_DoNotCorruptTheMarker()
    {
        new AppSessionMarker(_dir, ClockAt(9), "v1")
            .MarkCrashed("boom\r\ncleanExit=true\nstack line");

        var next = new AppSessionMarker(_dir, ClockAt(10), "v1");

        // An injected "cleanExit=true" line must not be able to hide a crash.
        Assert.IsTrue(next.PreviousSessionCrashed);
    }
}
