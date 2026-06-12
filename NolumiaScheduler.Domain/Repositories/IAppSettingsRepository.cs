using NolumiaScheduler.Domain.ValueObjects;

namespace NolumiaScheduler.Domain.Repositories;

public interface IAppSettingsRepository
{
    ThemeMode GetThemeMode();
    void SaveThemeMode(ThemeMode mode);
    string? GetLanguage();
    void SaveLanguage(string? languageTag);

    /// <summary>
    /// View shown at startup: "Month", "Week" or "Weekdays". Null means the
    /// application default (Week). Stored as a string so the domain stays free of
    /// presentation enums.
    /// </summary>
    string? GetStartupView();
    void SaveStartupView(string? view);
}
