using Microsoft.UI.Xaml;
using NolumiaScheduler.Domain.Repositories;
using NolumiaScheduler.Domain.ValueObjects;
using NolumiaScheduler.Presentation.Helpers;

namespace NolumiaScheduler.WinUI.Presentation.Services;

/// <summary>
/// Applies and persists the visual theme. There is no settings UI yet, but the
/// preference is stored as data (settings.json) so a future UI — or editing the
/// file by hand — switches the whole app between System/Light/Dark.
/// </summary>
public class ThemeService
{
    private readonly IAppSettingsRepository _settings;
    private Window? _window;

    public ThemeService(IAppSettingsRepository settings) => _settings = settings;

    public ThemeMode CurrentMode { get; private set; } = ThemeMode.System;

    /// <summary>Reads the persisted preference and applies it to the window's content root.</summary>
    public void Initialize(Window window)
    {
        _window = window;
        Apply(_settings.GetThemeMode());
    }

    /// <summary>Switches the theme at runtime and persists the choice.</summary>
    public void SetTheme(ThemeMode mode)
    {
        Apply(mode);
        _settings.SaveThemeMode(mode);
    }

    private void Apply(ThemeMode mode)
    {
        CurrentMode = mode;
        var elementTheme = mode switch
        {
            ThemeMode.Light => ElementTheme.Light,
            ThemeMode.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };

        // Canvas-drawn views and view models resolve colors through ThemeHelper,
        // so keep it in sync with the XAML element theme.
        ThemeHelper.UpdateTheme(elementTheme);

        if (_window?.Content is FrameworkElement root)
            root.RequestedTheme = elementTheme;
    }
}
