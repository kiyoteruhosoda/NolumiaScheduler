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

    public IReadOnlyList<CalendarEventRow> FindByPeriod(int fromDay, int toDay)
    {
        using var connection = _connectionFactory.Open();
        using var command = connection.CreateCommand();
        // Overlap test against the indexed active span: the event's span starts on or
        // before the window end and ends on or after the window start.
        command.CommandText = """
            SELECT id, payload, updated_at FROM calendar_events
            WHERE span_start_day <= $to_day AND span_end_day >= $from_day;
            """;
        command.Parameters.AddWithValue("$from_day", fromDay);
        command.Parameters.AddWithValue("$to_day", toDay);

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
            INSERT INTO calendar_events (id, payload, updated_at, span_start_day, span_end_day)
            VALUES ($id, $payload, $updated_at, $span_start_day, $span_end_day)
            ON CONFLICT(id) DO UPDATE SET
                payload = excluded.payload,
                updated_at = excluded.updated_at,
                span_start_day = excluded.span_start_day,
                span_end_day = excluded.span_end_day;
            """;
        command.Parameters.AddWithValue("$id", row.Id);
        command.Parameters.AddWithValue("$payload", row.Payload);
        command.Parameters.AddWithValue("$updated_at", row.UpdatedAt);
        command.Parameters.AddWithValue("$span_start_day", row.SpanStartDay);
        command.Parameters.AddWithValue("$span_end_day", row.SpanEndDay);
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
