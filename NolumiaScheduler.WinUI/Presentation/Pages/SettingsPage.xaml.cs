using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using NolumiaScheduler.Domain.ValueObjects;
using NolumiaScheduler.Presentation.Resources.Strings;
using NolumiaScheduler.WinUI.Presentation.Services;

namespace NolumiaScheduler.WinUI.Presentation.Pages;

public sealed partial class SettingsPage : Page
{
    private ThemeService? _themeService;
    private bool _suppressThemeChange;

    public SettingsPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        _themeService = App.Services.GetRequiredService<ThemeService>();

        PageTitleText.Text  = AppResources.SettingsTitle;
        ThemeLabelText.Text = AppResources.ThemeLabel;

        _suppressThemeChange = true;
        ThemePicker.Items.Clear();
        ThemePicker.Items.Add(AppResources.ThemeSystem);
        ThemePicker.Items.Add(AppResources.ThemeLight);
        ThemePicker.Items.Add(AppResources.ThemeDark);
        ThemePicker.SelectedIndex = _themeService.CurrentMode switch
        {
            ThemeMode.Light => 1,
            ThemeMode.Dark  => 2,
            _               => 0,
        };
        _suppressThemeChange = false;
    }

    private void OnThemeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressThemeChange || _themeService == null) return;

        var mode = ThemePicker.SelectedIndex switch
        {
            1 => ThemeMode.Light,
            2 => ThemeMode.Dark,
            _ => ThemeMode.System,
        };
        _themeService.SetTheme(mode);
    }
}
