using Microsoft.UI.Xaml;

namespace NolumiaScheduler.WinUI.Helpers;

public static class ThemeHelper
{
    private static ElementTheme _theme = ElementTheme.Default;

    public static void UpdateTheme(ElementTheme theme) => _theme = theme;

    public static bool IsDark => _theme == ElementTheme.Dark ||
        (_theme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);
}
