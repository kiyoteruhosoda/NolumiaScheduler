using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.Repositories;
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

    [TestMethod]
    public void FindByPeriod_既定実装はJSONでも重なりで絞り込む()
    {
        // JSON does not override FindByPeriod, so this exercises the default
        // interface implementation (FindAll + OverlapsPeriod) via the interface type.
        ICalendarEventRepository repo = new JsonCalendarEventRepository(Path.Combine(_root, "events"));
        repo.Save(NewSingleEventOn(new DateOnly(2026, 6, 12)));
        repo.Save(NewSingleEventOn(new DateOnly(2026, 9, 1)));

        var hits = repo.FindByPeriod(new LocalDateValue(2026, 6, 10), new LocalDateValue(2026, 6, 16));

        Assert.HasCount(1, hits);
    }

    private static DateTimeOffset Anchor(int y, int mo, int d, int h, int mi, TimeZoneId tz)
    {
        if (tz.Value == "UTC")
            return new DateTimeOffset(y, mo, d, h, mi, 0, TimeSpan.Zero);
        return LocalSchedulePoint
            .StartInstant(new LocalDateValue(y, mo, d), new LocalTimeValue(h, mi, 0), tz.ToTimeZoneInfo())
            .ToUniversalTime();
    }

    private static CalendarEvent NewSingleEventOn(DateOnly date)
    {
        var tz = new TimeZoneId("UTC");
        return CalendarEvent.CreateSingle(
            new EventId(Guid.NewGuid().ToString()),
            new EventTitle("Single"),
            location: null,
            Visibility.Public,
            eventType: null,
            description: null,
            tz,
            new SingleEventSchedule(
                Anchor(date.Year, date.Month, date.Day, 10, 0, tz), 60),
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    private static CalendarEvent NewSingleEvent(string title)
    {
        var now = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        var tz = new TimeZoneId("Asia/Tokyo");
        return CalendarEvent.CreateSingle(
            new EventId(Guid.NewGuid().ToString()),
            new EventTitle(title),
            location: null,
            Visibility.Public,
            eventType: null,
            description: null,
            tz,
            new SingleEventSchedule(Anchor(2026, 5, 20, 10, 0, tz), 60),
            now);
    }
}
