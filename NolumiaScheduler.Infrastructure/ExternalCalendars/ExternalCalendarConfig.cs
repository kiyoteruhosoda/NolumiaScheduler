using System.Text.Json;
using System.Text.Json.Serialization;

namespace NolumiaScheduler.Infrastructure.ExternalCalendars;

/// <summary>
/// Settings for sending the Outlook (Power Automate) JSON to the server, read from
/// <c>external-calendar.json</c> in the data directory. The file is created with defaults on first
/// run. Missing or invalid values fall back to defaults. <see cref="ServerCalendarId"/> is the
/// server calendar the feed goes to, remembered after the first send (server ADR-0054 decision 6).
/// </summary>
public sealed class ExternalCalendarConfig
{
    public const string FileName = "external-calendar.json";
    public const int DefaultIntervalMinutes = 15;

    public bool Enabled { get; init; }
    public string FilePath { get; init; } = "";
    public int IntervalMinutes { get; init; } = DefaultIntervalMinutes;
    public long? ServerCalendarId { get; init; }

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
            IntervalMinutes = dto?.IntervalMinutes is > 0 and var m ? m : DefaultIntervalMinutes,
            ServerCalendarId = dto?.ServerCalendarId is > 0 and var id ? id : null
        };
    }

    /// <summary>
    /// Writes <paramref name="serverCalendarId"/> into <c>external-calendar.json</c>, keeping the
    /// other values as they are in the file (null forgets it, e.g. after the calendar was deleted
    /// on the server).
    /// </summary>
    public static void SaveServerCalendarId(string dataDirectory, long? serverCalendarId)
    {
        Directory.CreateDirectory(dataDirectory);
        var path = Path.Combine(dataDirectory, FileName);
        ExternalCalendarConfigDto dto;
        try
        {
            dto = File.Exists(path)
                ? JsonSerializer.Deserialize(File.ReadAllText(path), ExternalCalendarConfigJsonContext.Default.ExternalCalendarConfigDto)
                    ?? new ExternalCalendarConfigDto()
                : new ExternalCalendarConfigDto { Enabled = true, FilePath = DefaultFilePath(), IntervalMinutes = DefaultIntervalMinutes };
        }
        catch (JsonException)
        {
            dto = new ExternalCalendarConfigDto { Enabled = true, FilePath = DefaultFilePath(), IntervalMinutes = DefaultIntervalMinutes };
        }
        dto.ServerCalendarId = serverCalendarId;
        File.WriteAllText(path, JsonSerializer.Serialize(dto, ExternalCalendarConfigJsonContext.Default.ExternalCalendarConfigDto));
    }
}

internal sealed class ExternalCalendarConfigDto
{
    public bool Enabled { get; set; }
    public string? FilePath { get; set; }
    public int? IntervalMinutes { get; set; }
    public long? ServerCalendarId { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ExternalCalendarConfigDto))]
internal partial class ExternalCalendarConfigJsonContext : JsonSerializerContext
{
}
