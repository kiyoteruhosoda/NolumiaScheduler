using NolumiaScheduler.Infrastructure.Sqlite.Db;

namespace NolumiaScheduler.Infrastructure.Sqlite.Repositories;

/// <summary>Owns the SQL for the <c>business_calendars</c> table.</summary>
internal sealed class BusinessCalendarDao(SqliteConnectionFactory connectionFactory)
{
    private readonly SqliteConnectionFactory _connectionFactory = connectionFactory;

    public BusinessCalendarRow? FindById(string id)
    {
        using var connection = _connectionFactory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, payload FROM business_calendars WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);

        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public IReadOnlyList<BusinessCalendarRow> FindAll()
    {
        using var connection = _connectionFactory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, payload FROM business_calendars;";

        var rows = new List<BusinessCalendarRow>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            rows.Add(Map(reader));
        return rows;
    }

    public void Upsert(BusinessCalendarRow row)
    {
        using var connection = _connectionFactory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO business_calendars (id, payload)
            VALUES ($id, $payload)
            ON CONFLICT(id) DO UPDATE SET
                payload = excluded.payload;
            """;
        command.Parameters.AddWithValue("$id", row.Id);
        command.Parameters.AddWithValue("$payload", row.Payload);
        command.ExecuteNonQuery();
    }

    public void Delete(string id)
    {
        using var connection = _connectionFactory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM business_calendars WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    private static BusinessCalendarRow Map(Microsoft.Data.Sqlite.SqliteDataReader reader)
        => new(reader.GetString(0), reader.GetString(1));
}
