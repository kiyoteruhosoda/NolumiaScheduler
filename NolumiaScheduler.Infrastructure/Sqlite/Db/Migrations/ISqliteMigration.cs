using Microsoft.Data.Sqlite;

namespace NolumiaScheduler.Infrastructure.Sqlite.Db.Migrations;

/// <summary>
/// A single, ordered, forward-only schema change. Each migration owns one
/// <see cref="Version"/>; the runner applies pending versions in ascending order
/// inside a transaction it supplies.
/// </summary>
internal interface ISqliteMigration
{
    int Version { get; }

    void Up(SqliteConnection connection, SqliteTransaction transaction);
}
