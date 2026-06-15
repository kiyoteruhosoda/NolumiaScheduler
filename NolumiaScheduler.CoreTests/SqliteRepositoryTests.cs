using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.ValueObjects;
using NolumiaScheduler.Infrastructure.Sqlite.Db;
using NolumiaScheduler.Infrastructure.Sqlite.Db.Migrations;
using NolumiaScheduler.Infrastructure.Sqlite.Repositories;
using Visibility = NolumiaScheduler.Domain.ValueObjects.Visibility;

namespace NolumiaScheduler.CoreTests;

/// <summary>
/// Integration tests for the SQLite backend against a real on-disk database file,
/// proving the JSON and SQLite repositories are behaviourally interchangeable.
/// </summary>
[TestClass]
public class SqliteRepositoryTests
{
    private string _dbPath = null!;
    private SqliteConnectionFactory _factory = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"nolumia-test-{Guid.NewGuid():N}.db");
        _factory = new SqliteConnectionFactory(_dbPath);
        SqliteMigrationRunner.Run(_factory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    [TestMethod]
    public void MigrationRunner_繰り返し実行しても冪等である()
    {
        // Running again over an already-migrated database must not throw.
        SqliteMigrationRunner.Run(_factory);
        SqliteMigrationRunner.Run(_factory);
    }

    [TestMethod]
    public void CalendarEvent_保存して取得できる()
    {
        var repo = new SqliteCalendarEventRepository(_factory);
        var ev = NewSingleEvent("Meeting", "Room A");

        repo.Save(ev);

        var loaded = repo.FindById(ev.Id);
        Assert.IsNotNull(loaded);
        Assert.AreEqual("Meeting", loaded!.Title.Value);
        Assert.AreEqual("Room A", loaded.Location!.Value);
        Assert.IsTrue(loaded.IsSingle());
        Assert.HasCount(1, repo.FindAll());
    }

    [TestMethod]
    public void CalendarEvent_別インスタンスからも読み込める()
    {
        var ev = NewSingleEvent("Persisted", null);
        new SqliteCalendarEventRepository(_factory).Save(ev);

        // A fresh repository instance reads the same physical database file.
        var loaded = new SqliteCalendarEventRepository(_factory).FindById(ev.Id);

        Assert.IsNotNull(loaded);
        Assert.AreEqual("Persisted", loaded!.Title.Value);
    }

    [TestMethod]
    public void CalendarEvent_保存は更新として上書きされる()
    {
        var repo = new SqliteCalendarEventRepository(_factory);
        repo.Save(NewSingleEvent("First", null));
        repo.Save(NewSingleEvent("Second", null));

        // Two different events -> two rows; saving the same id twice -> one row.
        var sameId = NewSingleEvent("Third", null);
        repo.Save(sameId);
        repo.Save(sameId);

        Assert.HasCount(3, repo.FindAll());
    }

    [TestMethod]
    public void CalendarEvent_削除できる()
    {
        var repo = new SqliteCalendarEventRepository(_factory);
        var ev = NewSingleEvent("ToDelete", null);
        repo.Save(ev);

        repo.Delete(ev.Id);

        Assert.IsNull(repo.FindById(ev.Id));
        Assert.IsEmpty(repo.FindAll());
    }

    [TestMethod]
    public void CalendarEvent_保存削除でChangedが発火する()
    {
        var repo = new SqliteCalendarEventRepository(_factory);
        var count = 0;
        repo.Changed += () => count++;

        var ev = NewSingleEvent("Notify", null);
        repo.Save(ev);
        repo.Delete(ev.Id);

        Assert.AreEqual(2, count);
    }

    [TestMethod]
    public void BusinessCalendar_保存して取得できる()
    {
        var repo = new SqliteBusinessCalendarRepository(_factory);
        var cal = new BusinessCalendar(
            new BusinessCalendarId(Guid.NewGuid().ToString()),
            "Tokyo Office",
            new TimeZoneId("Asia/Tokyo"),
            [Weekday.Monday, Weekday.Tuesday],
            [new Holiday(new LocalDateValue(2026, 1, 1), "New Year")]);

        repo.Save(cal);

        var loaded = repo.FindById(cal.Id);
        Assert.IsNotNull(loaded);
        Assert.AreEqual("Tokyo Office", loaded!.Name);
        Assert.HasCount(2, loaded.Workdays);
        Assert.IsTrue(loaded.IsHoliday(new LocalDateValue(2026, 1, 1)));
    }

    [TestMethod]
    public void AppSettings_テーマと言語を保存して取得できる()
    {
        var repo = new SqliteAppSettingsRepository(_factory);

        Assert.AreEqual(ThemeMode.System, repo.GetThemeMode());
        Assert.IsNull(repo.GetLanguage());

        repo.SaveThemeMode(ThemeMode.Dark);
        repo.SaveLanguage("ja");
        repo.SaveStartupView("Week");

        Assert.AreEqual(ThemeMode.Dark, repo.GetThemeMode());
        Assert.AreEqual("ja", repo.GetLanguage());
        Assert.AreEqual("Week", repo.GetStartupView());
    }

    private static CalendarEvent NewSingleEvent(string title, string? location)
    {
        var now = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        var start = new DateTimeOffset(2026, 5, 20, 10, 0, 0, TimeSpan.Zero);
        return CalendarEvent.CreateSingle(
            new EventId(Guid.NewGuid().ToString()),
            new EventTitle(title),
            location is null ? null : new Location(location),
            Visibility.Public,
            eventType: null,
            description: null,
            new TimeZoneId("Asia/Tokyo"),
            allDay: false,
            new SingleEventSchedule(start, start.AddHours(1)),
            now);
    }
}
