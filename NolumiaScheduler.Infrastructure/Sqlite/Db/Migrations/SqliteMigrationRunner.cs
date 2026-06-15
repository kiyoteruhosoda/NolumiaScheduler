using Microsoft.Data.Sqlite;

namespace NolumiaScheduler.Infrastructure.Sqlite.Db.Migrations;

/// <summary>
/// Applies pending <see cref="ISqliteMigration"/>s in ascending version order.
/// Applied versions are tracked in <c>schema_migrations</c> so every run is
/// idempotent and the database can evolve across releases without data loss.
/// </summary>
public static class SqliteMigrationRunner
{
    // Register new migrations here in ascending version order.
    private static readonly ISqliteMigration[] Migrations =
    [
        new M0001_InitialSchema(),
    ];

    public static void Run(SqliteConnectionFactory connectionFactory)
    {
        using var connection = connectionFactory.Open();

        EnsureMigrationTable(connection);
        var current = GetCurrentVersion(connection);

        foreach (var migration in Migrations.OrderBy(m => m.Version))
        {
            if (migration.Version <= current)
                continue;

            using var transaction = connection.BeginTransaction();
            migration.Up(connection, transaction);
            RecordVersion(connection, transaction, migration.Version);
            transaction.Commit();
        }
    }

    private static void EnsureMigrationTable(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "CREATE TABLE IF NOT EXISTS schema_migrations (version INTEGER PRIMARY KEY);";
        command.ExecuteNonQuery();
    }

    private static int GetCurrentVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;";
        var result = command.ExecuteScalar();
        return result is null or DBNull ? 0 : Convert.ToInt32(result);
    }

    private static void RecordVersion(SqliteConnection connection, SqliteTransaction transaction, int version)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO schema_migrations (version) VALUES ($version);";
        command.Parameters.AddWithValue("$version", version);
        command.ExecuteNonQuery();
    }
}
