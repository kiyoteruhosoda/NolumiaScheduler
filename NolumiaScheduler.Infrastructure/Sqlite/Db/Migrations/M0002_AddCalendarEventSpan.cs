using Microsoft.Data.Sqlite;
using NolumiaScheduler.Infrastructure.Sqlite.Repositories;

namespace NolumiaScheduler.Infrastructure.Sqlite.Db.Migrations;

/// <summary>
/// Adds indexed active-span columns to <c>calendar_events</c> so period queries can
/// pre-filter events without loading and expanding the whole store. Existing rows are
/// backfilled by deserializing their payload and recomputing the span; the wide
/// defaults keep any not-yet-backfilled row matching every window (never wrongly
/// excluded).
/// </summary>
internal sealed class M0002_AddCalendarEventSpan : ISqliteMigration
{
    public int Version => 2;

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        using (var ddl = connection.CreateCommand())
        {
            ddl.Transaction = transaction;
            ddl.CommandText = """
                ALTER TABLE calendar_events ADD COLUMN span_start_day INTEGER NOT NULL DEFAULT -2147483648;
                ALTER TABLE calendar_events ADD COLUMN span_end_day   INTEGER NOT NULL DEFAULT  2147483647;
                CREATE INDEX idx_calendar_events_span ON calendar_events (span_start_day, span_end_day);
                """;
            ddl.ExecuteNonQuery();
        }

        // Backfill: read each payload, recompute the span via the shared mapper.
        var rows = new List<(string Id, string Payload)>();
        using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = "SELECT id, payload FROM calendar_events;";
            using var reader = select.ExecuteReader();
            while (reader.Read())
                rows.Add((reader.GetString(0), reader.GetString(1)));
        }

        foreach (var (id, payload) in rows)
        {
            var calendarEvent = CalendarEventRowMapper.ToDomain(new CalendarEventRow(id, payload, string.Empty));
            var (spanStartDay, spanEndDay) = calendarEvent.GetIndexedDaySpan();

            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE calendar_events
                SET span_start_day = $span_start_day, span_end_day = $span_end_day
                WHERE id = $id;
                """;
            update.Parameters.AddWithValue("$span_start_day", spanStartDay);
            update.Parameters.AddWithValue("$span_end_day", spanEndDay);
            update.Parameters.AddWithValue("$id", id);
            update.ExecuteNonQuery();
        }
    }
}
