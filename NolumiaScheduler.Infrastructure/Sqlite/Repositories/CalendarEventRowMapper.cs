using System.Text.Json;
using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Infrastructure.Json.Repositories;

namespace NolumiaScheduler.Infrastructure.Sqlite.Repositories;

/// <summary>
/// Converts between <see cref="CalendarEventRow"/> and the domain aggregate.
/// The deeply nested, polymorphic recurrence model is mapped through the same
/// <see cref="CalendarEventDto"/> the JSON backend uses, so there is a single source
/// of truth for (de)serialization across both storage backends.
/// </summary>
internal static class CalendarEventRowMapper
{
    public static CalendarEvent ToDomain(CalendarEventRow row)
    {
        var dto = JsonSerializer.Deserialize(row.Payload, AppJsonContext.Default.CalendarEventDto)
            ?? throw new InvalidOperationException($"Corrupted calendar event payload for id '{row.Id}'.");
        return dto.ToDomain();
    }

    public static CalendarEventRow FromDomain(CalendarEvent calendarEvent)
    {
        var dto = CalendarEventDto.FromDomain(calendarEvent);
        var payload = JsonSerializer.Serialize(dto, AppJsonContext.Default.CalendarEventDto);
        var (spanStartDay, spanEndDay) = calendarEvent.GetIndexedDaySpan();
        return new CalendarEventRow(
            calendarEvent.Id.Value,
            payload,
            calendarEvent.UpdatedAt.ToString("O"),
            spanStartDay,
            spanEndDay);
    }
}
