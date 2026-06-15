using System.Text.Json;
using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Infrastructure.Json.Repositories;

namespace NolumiaScheduler.Infrastructure.Sqlite.Repositories;

/// <summary>
/// Converts between <see cref="BusinessCalendarRow"/> and the domain aggregate,
/// reusing the JSON backend's <see cref="BusinessCalendarDto"/> for the payload.
/// </summary>
internal static class BusinessCalendarRowMapper
{
    public static BusinessCalendar ToDomain(BusinessCalendarRow row)
    {
        var dto = JsonSerializer.Deserialize(row.Payload, AppJsonContext.Default.BusinessCalendarDto)
            ?? throw new InvalidOperationException($"Corrupted business calendar payload for id '{row.Id}'.");
        return dto.ToDomain();
    }

    public static BusinessCalendarRow FromDomain(BusinessCalendar calendar)
    {
        var dto = BusinessCalendarDto.FromDomain(calendar);
        var payload = JsonSerializer.Serialize(dto, AppJsonContext.Default.BusinessCalendarDto);
        return new BusinessCalendarRow(calendar.Id.Value, payload);
    }
}
