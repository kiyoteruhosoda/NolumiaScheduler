using System.Text.Json;
using System.Text.Json.Serialization;

namespace NolumiaScheduler.Infrastructure.ExternalCalendars;

/// <summary>
/// Settings for external calendar import, read from <c>external-calendar.json</c> in the data
/// directory. The file is created with defaults on first run
/// Missing or invalid values fall back to defaults.
/// </summary>
public sealed class ExternalCalendarConfig
{
    public const string FileName = "external-calendar.json";
    public const int DefaultIntervalMinutes = 15;

    public bool Enabled { get; init; }
    public string FilePath { get; init; } = "";
    public int IntervalMinutes { get; init; } = DefaultIntervalMinutes;

    public static string DefaultFilePath()
    {
        var oneDrive = Environment.GetEnvironmentVariable("OneDriveCommercial")
            ?? Environment.GetEnvironmentVariable("OneDrive")
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(oneDrive, "NolumiaScheduler", "outlook-calendar.json");
    }

    public static ExternalCalendarConfig Load(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        var path = Path.Combine(dataDirectory, FileName);
        ExternalCalendarConfigDto? dto = null;

        if (File.Exists(path))
        {
            try
            {
                dto = JsonSerializer.Deserialize(File.ReadAllText(path), ExternalCalendarConfigJsonContext.Default.ExternalCalendarConfigDto);
            }
            catch (JsonException)
            {
                // A corrupted config must not block startup; treat as disabled.
                return new ExternalCalendarConfig { FilePath = DefaultFilePath() };
            }
        }
        else
        {
            // Enabled by default: a missing feed file is harmless (sync just skips).
            dto = new ExternalCalendarConfigDto { Enabled = true, FilePath = DefaultFilePath(), IntervalMinutes = DefaultIntervalMinutes };
            try
            {
                File.WriteAllText(path, JsonSerializer.Serialize(dto, ExternalCalendarConfigJsonContext.Default.ExternalCalendarConfigDto));
            }
            catch (IOException)
            {
            }
        }

        return new ExternalCalendarConfig
        {
            Enabled = dto?.Enabled ?? false,
            FilePath = string.IsNullOrWhiteSpace(dto?.FilePath)
                ? DefaultFilePath()
                : Environment.ExpandEnvironmentVariables(dto.FilePath),
            IntervalMinutes = dto?.IntervalMinutes is > 0 and var m ? m : DefaultIntervalMinutes
        };
    }
}

internal sealed class ExternalCalendarConfigDto
{
    public bool Enabled { get; set; }
    public string? FilePath { get; set; }
    public int? IntervalMinutes { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ExternalCalendarConfigDto))]
internal partial class ExternalCalendarConfigJsonContext : JsonSerializerContext
{
}
