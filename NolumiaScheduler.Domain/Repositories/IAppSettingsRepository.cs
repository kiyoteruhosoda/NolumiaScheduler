using NolumiaScheduler.Domain.ValueObjects;

namespace NolumiaScheduler.Domain.Repositories;

public interface IAppSettingsRepository
{
    ThemeMode GetThemeMode();
    void SaveThemeMode(ThemeMode mode);
}
