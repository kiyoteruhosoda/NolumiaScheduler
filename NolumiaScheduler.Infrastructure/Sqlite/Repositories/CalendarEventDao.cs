using NolumiaScheduler.Infrastructure.Sqlite.Db;

namespace NolumiaScheduler.Infrastructure.Sqlite.Repositories;

/// <summary>
/// Owns the SQL for the <c>calendar_events</c> table. Returns/accepts only
/// <see cref="CalendarEventRow"/>; the repository layer handles domain mapping.
/// </summary>
internal sealed class CalendarEventDao(SqliteConnectionFactory connectionFactory)
{
    private readonly SqliteConnectionFactory _connectionFactory = connectionFactory;

    public CalendarEventRow? FindById(string id)
    {
        using var connection = _connectionFactory.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, payload, updated_at FROM calendar_events WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);

        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public IReadOnlyList<CalendarEventRow> FindAll()
    {
        using var connection = _connectionFactory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, payload, updated_at FROM calendar_events;";

        var rows = new List<CalendarEventRow>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            rows.Add(Map(reader));
        return rows;
    }

    public void Upsert(CalendarEventRow row)
    {
        using var connection = _connectionFactory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO calendar_events (id, payload, updated_at)
            VALUES ($id, $payload, $updated_at)
            ON CONFLICT(id) DO UPDATE SET
                payload = excluded.payload,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$id", row.Id);
        command.Parameters.AddWithValue("$payload", row.Payload);
        command.Parameters.AddWithValue("$updated_at", row.UpdatedAt);
        command.ExecuteNonQuery();
    }

    public void Delete(string id)
    {
        using var connection = _connectionFactory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM calendar_events WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    private static CalendarEventRow Map(Microsoft.Data.Sqlite.SqliteDataReader reader)
        => new(reader.GetString(0), reader.GetString(1), reader.GetString(2));
}
