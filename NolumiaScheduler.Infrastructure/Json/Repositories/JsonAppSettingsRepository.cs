using System.Text.Json;
using NolumiaScheduler.Domain.Repositories;
using NolumiaScheduler.Domain.ValueObjects;

namespace NolumiaScheduler.Infrastructure.Json.Repositories;

public class JsonAppSettingsRepository : IAppSettingsRepository
{
    private readonly string _filePath;

    public JsonAppSettingsRepository(string directoryPath)
    {
        Directory.CreateDirectory(directoryPath);
        _filePath = Path.Combine(directoryPath, "settings.json");
    }

    public ThemeMode GetThemeMode()
    {
        var dto = Load();
        return dto?.Theme != null && Enum.TryParse<ThemeMode>(dto.Theme, out var mode)
            ? mode
            : ThemeMode.System;
    }

    public void SaveThemeMode(ThemeMode mode)
    {
        var dto = Load() ?? new AppSettingsDto();
        dto.Theme = mode.ToString();
        File.WriteAllText(_filePath, JsonSerializer.Serialize(dto, AppJsonContext.Default.AppSettingsDto));
    }

    private AppSettingsDto? Load()
    {
        if (!File.Exists(_filePath)) return null;
        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(_filePath), AppJsonContext.Default.AppSettingsDto);
        }
        catch (JsonException)
        {
            // A corrupted settings file must not block startup; fall back to defaults.
            return null;
        }
    }
}

internal class AppSettingsDto
{
    public string? Theme { get; set; }
}
