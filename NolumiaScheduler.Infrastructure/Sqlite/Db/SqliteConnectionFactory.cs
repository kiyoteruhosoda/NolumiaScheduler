using Microsoft.Data.Sqlite;

namespace NolumiaScheduler.Infrastructure.Sqlite.Db;

/// <summary>
/// Creates open connections to the application's SQLite database file.
/// Keeping connection creation behind a factory means repositories never own the
/// connection string and stay easy to point at an in-memory database in tests.
/// </summary>
public sealed class SqliteConnectionFactory
{
    private readonly string _connectionString;

    public SqliteConnectionFactory(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
        }.ToString();
    }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }
}
