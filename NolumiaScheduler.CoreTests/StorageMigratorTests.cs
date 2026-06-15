using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.ValueObjects;
using NolumiaScheduler.Infrastructure;
using NolumiaScheduler.Infrastructure.Json.Repositories;
using NolumiaScheduler.Infrastructure.Sqlite.Db;
using NolumiaScheduler.Infrastructure.Sqlite.Db.Migrations;
using NolumiaScheduler.Infrastructure.Sqlite.Repositories;
using Visibility = NolumiaScheduler.Domain.ValueObjects.Visibility;

namespace NolumiaScheduler.CoreTests;

[TestClass]
public class StorageMigratorTests
{
    private string _root = null!;

    [TestInitialize]
    public void Setup()
        => _root = Path.Combine(Path.GetTempPath(), $"nolumia-migrate-{Guid.NewGuid():N}");

    [TestCleanup]
    public void Cleanup()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [TestMethod]
    public void Migrate_JSONからSQLiteへ全データを移行する()
    {
        // Arrange: populate a JSON store.
        var jsonEvents = new JsonCalendarEventRepository(Path.Combine(_root, "events"));
        var jsonCalendars = new JsonBusinessCalendarRepository(Path.Combine(_root, "business-calendars"));
        var jsonSettings = new JsonAppSettingsRepository(_root);

        jsonEvents.Save(NewSingleEvent("Meeting"));
        jsonEvents.Save(NewSingleEvent("Review"));
        jsonCalendars.Save(new BusinessCalendar(
            new BusinessCalendarId(Guid.NewGuid().ToString()),
            "Tokyo",
            new TimeZoneId("Asia/Tokyo"),
            [Weekday.Monday]));
        jsonSettings.SaveThemeMode(ThemeMode.Dark);
        jsonSettings.SaveLanguage("ja");

        var factory = new SqliteConnectionFactory(Path.Combine(_root, "nolumia.db"));
        SqliteMigrationRunner.Run(factory);
        var sqliteEvents = new SqliteCalendarEventRepository(factory);
        var sqliteCalendars = new SqliteBusinessCalendarRepository(factory);
        var sqliteSettings = new SqliteAppSettingsRepository(factory);

        // Act
        var report = StorageMigrator.Migrate(
            jsonEvents, jsonCalendars, jsonSettings,
            sqliteEvents, sqliteCalendars, sqliteSettings);

        // Assert
        Assert.AreEqual(2, report.Events);
        Assert.AreEqual(1, report.BusinessCalendars);
        Assert.HasCount(2, sqliteEvents.FindAll());
        Assert.HasCount(1, sqliteCalendars.FindAll());
        Assert.AreEqual(ThemeMode.Dark, sqliteSettings.GetThemeMode());
        Assert.AreEqual("ja", sqliteSettings.GetLanguage());
    }

    private static CalendarEvent NewSingleEvent(string title)
    {
        var now = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        var start = new DateTimeOffset(2026, 5, 20, 10, 0, 0, TimeSpan.Zero);
        return CalendarEvent.CreateSingle(
            new EventId(Guid.NewGuid().ToString()),
            new EventTitle(title),
            location: null,
            Visibility.Public,
            eventType: null,
            description: null,
            new TimeZoneId("Asia/Tokyo"),
            allDay: false,
            new SingleEventSchedule(start, start.AddHours(1)),
            now);
    }
}
