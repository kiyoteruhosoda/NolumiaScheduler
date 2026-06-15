using NolumiaScheduler.Domain.Repositories;
using NolumiaScheduler.Infrastructure.Json.Repositories;
using NolumiaScheduler.Infrastructure.Sqlite.Db;
using NolumiaScheduler.Infrastructure.Sqlite.Db.Migrations;
using NolumiaScheduler.Infrastructure.Sqlite.Repositories;

namespace NolumiaScheduler.Infrastructure;

/// <summary>
/// Single source of truth for where data lives and how to build repositories for a
/// given <see cref="StorageBackend"/>. Used by the app's composition root and by the
/// management CLI so both agree on file locations and SQLite schema setup.
/// </summary>
public sealed class StorageContext
{
    private SqliteConnectionFactory? _sqliteFactory;

    public StorageContext(string dataDirectory)
    {
        DataDirectory = dataDirectory;
    }

    /// <summary>Default per-user data folder (e.g. %LOCALAPPDATA%\NolumiaScheduler).</summary>
    public static string DefaultDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NolumiaScheduler");

    public static StorageContext Default => new(DefaultDataDirectory);

    public string DataDirectory { get; }
    public string EventsDirectory => Path.Combine(DataDirectory, "events");
    public string BusinessCalendarsDirectory => Path.Combine(DataDirectory, "business-calendars");
    public string SqliteDatabasePath => Path.Combine(DataDirectory, "nolumia.db");

    /// <summary>Bootstrap config that selects the active backend (see <see cref="StorageConfig"/>).</summary>
    public StorageConfig Config => new(DataDirectory);

    /// <summary>
    /// Shared SQLite connection factory, created and schema-migrated on first use so a
    /// single context reuses one migrated database across repositories.
    /// </summary>
    public SqliteConnectionFactory SqliteConnectionFactory
    {
        get
        {
            if (_sqliteFactory == null)
            {
                _sqliteFactory = new SqliteConnectionFactory(SqliteDatabasePath);
                SqliteMigrationRunner.Run(_sqliteFactory);
            }
            return _sqliteFactory;
        }
    }

    public ICalendarEventRepository CreateCalendarEventRepository(StorageBackend backend) => backend switch
    {
        StorageBackend.Sqlite => new SqliteCalendarEventRepository(SqliteConnectionFactory),
        _ => new JsonCalendarEventRepository(EventsDirectory),
    };

    public IBusinessCalendarRepository CreateBusinessCalendarRepository(StorageBackend backend) => backend switch
    {
        StorageBackend.Sqlite => new SqliteBusinessCalendarRepository(SqliteConnectionFactory),
        _ => new JsonBusinessCalendarRepository(BusinessCalendarsDirectory),
    };

    public IAppSettingsRepository CreateAppSettingsRepository(StorageBackend backend) => backend switch
    {
        StorageBackend.Sqlite => new SqliteAppSettingsRepository(SqliteConnectionFactory),
        _ => new JsonAppSettingsRepository(DataDirectory),
    };
}
