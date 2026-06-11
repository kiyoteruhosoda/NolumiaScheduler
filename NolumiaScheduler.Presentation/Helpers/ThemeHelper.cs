using Microsoft.UI.Xaml;

namespace NolumiaScheduler.Presentation.Helpers;

public static class ThemeHelper
{
    private static ElementTheme _theme = ElementTheme.Default;
    private static Func<bool>? _isSystemDark;

    public static void UpdateTheme(ElementTheme theme) => _theme = theme;

    /// <summary>
    /// Supplies the system theme consulted when the app theme is <see cref="ElementTheme.Default"/>.
    /// The WinUI host registers a resolver backed by Application.Current at startup; unit tests
    /// run without one (treated as light) or register a stub to exercise dark-mode rendering.
    /// Resolving through a delegate keeps view models free of any Application.Current dependency.
    /// </summary>
    public static void UseSystemThemeSource(Func<bool> isSystemDark) => _isSystemDark = isSystemDark;

    public static bool IsDark => _theme switch
    {
        ElementTheme.Dark => true,
        ElementTheme.Light => false,
        _ => _isSystemDark?.Invoke() ?? false,
    };
}
