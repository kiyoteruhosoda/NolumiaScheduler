using System.Text.Json;
using System.Text.Json.Serialization;

namespace NolumiaScheduler.Infrastructure;

/// <summary>
/// Bootstrap configuration that selects which <see cref="StorageBackend"/> the app uses.
/// It lives in a small <c>storage.json</c> file that is independent of the backends
/// themselves, because the per-backend settings store cannot decide which backend to
/// load (chicken-and-egg). Missing or invalid config falls back to
/// <see cref="StorageBackend.Json"/>.
/// </summary>
public sealed class StorageConfig
{
    public StorageConfig(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        FilePath = Path.Combine(dataDirectory, "storage.json");
    }

    public string FilePath { get; }

    public StorageBackend GetBackend()
    {
        if (!File.Exists(FilePath)) return StorageBackend.Json;
        try
        {
            var dto = JsonSerializer.Deserialize(File.ReadAllText(FilePath), StorageConfigJsonContext.Default.StorageConfigDto);
            return Enum.TryParse<StorageBackend>(dto?.Backend, ignoreCase: true, out var backend)
                ? backend
                : StorageBackend.Json;
        }
        catch (JsonException)
        {
            // A corrupted config must not block startup; fall back to the default.
            return StorageBackend.Json;
        }
    }

    public void SetBackend(StorageBackend backend)
    {
        var json = JsonSerializer.Serialize(
            new StorageConfigDto { Backend = backend.ToString() },
            StorageConfigJsonContext.Default.StorageConfigDto);
        File.WriteAllText(FilePath, json);
    }
}

internal sealed class StorageConfigDto
{
    public string? Backend { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(StorageConfigDto))]
internal partial class StorageConfigJsonContext : JsonSerializerContext
{
}
