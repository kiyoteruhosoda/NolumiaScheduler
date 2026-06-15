using Microsoft.Data.Sqlite;

namespace NolumiaScheduler.Infrastructure.Sqlite.Db.Migrations;

/// <summary>
/// Initial schema. Aggregates are stored one row per aggregate with the domain
/// payload serialized to JSON (the same DTO shape the JSON backend writes to disk),
/// so both backends share a single, well-tested mapping for the deeply nested,
/// polymorphic recurrence model. The <c>id</c> columns keep lookups and deletes cheap.
/// Settings are stored as a simple key/value table.
/// </summary>
internal sealed class M0001_InitialSchema : ISqliteMigration
{
    public int Version => 1;

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE calendar_events (
                id          TEXT PRIMARY KEY,
                payload     TEXT NOT NULL,
                updated_at  TEXT NOT NULL
            );

            CREATE TABLE business_calendars (
                id          TEXT PRIMARY KEY,
                payload     TEXT NOT NULL
            );

            CREATE TABLE app_settings (
                key         TEXT PRIMARY KEY,
                value       TEXT
            );
            """;
        command.ExecuteNonQuery();
    }
}
