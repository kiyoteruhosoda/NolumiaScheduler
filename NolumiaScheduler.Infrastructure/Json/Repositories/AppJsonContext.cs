using System.Text.Json.Serialization;

namespace NolumiaScheduler.Infrastructure.Json.Repositories;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CalendarEventDto))]
[JsonSerializable(typeof(BusinessCalendarDto))]
[JsonSerializable(typeof(AppSettingsDto))]
internal partial class AppJsonContext : JsonSerializerContext
{
}
