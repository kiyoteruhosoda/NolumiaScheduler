using NolumiaScheduler.Infrastructure.Diagnostics;

namespace NolumiaScheduler.CoreTests;

[TestClass]
public class FileAppLogTests
{
    private string _dir = null!;

    [TestInitialize]
    public void Setup()
        => _dir = Path.Combine(Path.GetTempPath(), $"nolumia-log-{Guid.NewGuid():N}");

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private static FakeClock ClockAt(int year, int month, int day, int hour = 12)
        => new(new DateTimeOffset(year, month, day, hour, 0, 0, TimeSpan.Zero));

    [TestMethod]
    public void Write_CreatesDirectoryAndDayStampedFile()
    {
        var log = new FileAppLog(_dir, ClockAt(2026, 7, 30));

        log.Info(AppLogCategories.Lifecycle, "started");

        var path = Path.Combine(_dir, "nolumia-20260730.log");
        Assert.IsTrue(File.Exists(path), "expected a file named after the local day");
        StringAssert.Contains(File.ReadAllText(path), "started");
    }

    [TestMethod]
    public void Write_AppendsRatherThanReplacing()
    {
        var log = new FileAppLog(_dir, ClockAt(2026, 7, 30));

        log.Info(AppLogCategories.Lifecycle, "first");
        log.Info(AppLogCategories.Lifecycle, "second");

        var content = File.ReadAllText(log.CurrentFilePath);
        StringAssert.Contains(content, "first");
        StringAssert.Contains(content, "second");
    }

    [TestMethod]
    public void Write_IncludesLevelCategoryAndException()
    {
        var log = new FileAppLog(_dir, ClockAt(2026, 7, 30));

        log.Error(AppLogCategories.Crash, "boom", new InvalidOperationException("inner detail"));

        var content = File.ReadAllText(log.CurrentFilePath);
        StringAssert.Contains(content, "ERROR");
        StringAssert.Contains(content, "[Crash]");
        StringAssert.Contains(content, "boom");
        StringAssert.Contains(content, "InvalidOperationException");
        StringAssert.Contains(content, "inner detail");
    }

    [TestMethod]
    public void Write_DropsRecordsBelowTheMinimumLevel()
    {
        var log = new FileAppLog(_dir, ClockAt(2026, 7, 30), AppLogLevel.Warning);

        log.Info(AppLogCategories.Health, "routine sample");
        log.Warning(AppLogCategories.Health, "resource warning");

        var content = File.ReadAllText(log.CurrentFilePath);
        Assert.IsFalse(content.Contains("routine sample"), "info should have been filtered out");
        StringAssert.Contains(content, "resource warning");
    }

    [TestMethod]
    public void Write_RollsOverToANewFileOnTheNextDay()
    {
        var clock = ClockAt(2026, 7, 30);
        var log = new FileAppLog(_dir, clock);

        log.Info(AppLogCategories.Lifecycle, "day one");
        clock.Advance(TimeSpan.FromDays(1));
        log.Info(AppLogCategories.Lifecycle, "day two");

        StringAssert.Contains(File.ReadAllText(Path.Combine(_dir, "nolumia-20260730.log")), "day one");
        StringAssert.Contains(File.ReadAllText(Path.Combine(_dir, "nolumia-20260731.log")), "day two");
    }

    [TestMethod]
    public void Write_PrunesFilesOlderThanTheRetentionWindow()
    {
        var clock = ClockAt(2026, 7, 30);
        var log = new FileAppLog(_dir, clock, AppLogLevel.Info, retentionDays: 3);

        Directory.CreateDirectory(_dir);
        var expired = Path.Combine(_dir, "nolumia-20260701.log");
        var withinWindow = Path.Combine(_dir, "nolumia-20260729.log");
        var unrelated = Path.Combine(_dir, "notes.txt");
        File.WriteAllText(expired, "old");
        File.WriteAllText(withinWindow, "recent");
        File.WriteAllText(unrelated, "keep me");

        log.Info(AppLogCategories.Lifecycle, "trigger prune");

        Assert.IsFalse(File.Exists(expired), "expired log should be deleted");
        Assert.IsTrue(File.Exists(withinWindow), "log inside the retention window should be kept");
        Assert.IsTrue(File.Exists(unrelated), "unrelated files must not be touched");
    }

    [TestMethod]
    public void Write_DoesNotThrowWhenTheDirectoryIsUnusable()
    {
        // A file where the directory should be makes every write fail. The logger's contract is
        // that it can never be the reason the app goes down — least of all inside a crash handler.
        var blocked = Path.Combine(_dir, "blocked");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(blocked, "not a directory");

        var log = new FileAppLog(blocked, ClockAt(2026, 7, 30));

        log.Fatal(AppLogCategories.Crash, "crash while logging is broken", new Exception("boom"));
    }
}
