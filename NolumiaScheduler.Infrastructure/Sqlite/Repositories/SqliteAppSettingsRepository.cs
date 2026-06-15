using NolumiaScheduler.Domain.Repositories;
using NolumiaScheduler.Domain.ValueObjects;
using NolumiaScheduler.Infrastructure.Sqlite.Db;

namespace NolumiaScheduler.Infrastructure.Sqlite.Repositories;

/// <summary>
/// SQLite-backed <see cref="IAppSettingsRepository"/>. Settings are stored as rows
/// in the <c>app_settings</c> key/value table.
/// </summary>
public sealed class SqliteAppSettingsRepository : IAppSettingsRepository
{
    private const string ThemeKey = "theme";
    private const string LanguageKey = "language";
    private const string StartupViewKey = "startupView";

    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteAppSettingsRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public ThemeMode GetThemeMode()
    {
        var raw = Get(ThemeKey);
        return raw != null && Enum.TryParse<ThemeMode>(raw, out var mode) ? mode : ThemeMode.System;
    }

    public void SaveThemeMode(ThemeMode mode) => Set(ThemeKey, mode.ToString());

    public string? GetLanguage() => Get(LanguageKey);

    public void SaveLanguage(string? languageTag) => Set(LanguageKey, languageTag);

    public string? GetStartupView() => Get(StartupViewKey);

    public void SaveStartupView(string? view) => Set(StartupViewKey, view);

    private string? Get(string key)
    {
        using var connection = _connectionFactory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM app_settings WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);

        var result = command.ExecuteScalar();
        return result is null or DBNull ? null : (string)result;
    }

    private void Set(string key, string? value)
    {
        using var connection = _connectionFactory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO app_settings (key, value)
            VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET
                value = excluded.value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", (object?)value ?? DBNull.Value);
        command.ExecuteNonQuery();
    }
}
