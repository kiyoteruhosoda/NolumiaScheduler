using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using NolumiaScheduler.Domain.Repositories;
using NolumiaScheduler.Domain.ValueObjects;
using NolumiaScheduler.Presentation.Resources.Strings;
using NolumiaScheduler.WinUI.Presentation.Services;

namespace NolumiaScheduler.WinUI.Presentation.Pages;

public sealed partial class SettingsPage : Page
{
    // (tag, display-name) pairs in a fixed order matching picker indices
    private static readonly (string? Tag, string Name)[] LanguageOptions =
    [
        (null,  "English"),
        ("ja",  "日本語"),
    ];

    private ThemeService? _themeService;
    private IAppSettingsRepository? _settingsRepo;
    private bool _suppress;

    public SettingsPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        _themeService  = App.Services.GetRequiredService<ThemeService>();
        _settingsRepo  = App.Services.GetRequiredService<IAppSettingsRepository>();

        _suppress = true;

        PageTitleText.Text    = AppResources.SettingsTitle;
        ThemeLabelText.Text   = AppResources.ThemeLabel;
        LanguageLabelText.Text = AppResources.LanguageLabel;

        // Theme picker
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

        // Language picker
        LanguagePicker.Items.Clear();
        var savedTag = _settingsRepo.GetLanguage();
        var selectedIdx = 0;
        for (var i = 0; i < LanguageOptions.Length; i++)
        {
            LanguagePicker.Items.Add(LanguageOptions[i].Name);
            if (LanguageOptions[i].Tag == savedTag) selectedIdx = i;
        }
        LanguagePicker.SelectedIndex = selectedIdx;

        _suppress = false;
    }

    private void OnThemeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || _themeService == null) return;

        var mode = ThemePicker.SelectedIndex switch
        {
            1 => ThemeMode.Light,
            2 => ThemeMode.Dark,
            _ => ThemeMode.System,
        };
        _themeService.SetTheme(mode);
    }

    private void OnLanguageSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || _settingsRepo == null) return;

        var idx = LanguagePicker.SelectedIndex;
        if (idx < 0 || idx >= LanguageOptions.Length) return;

        var tag = LanguageOptions[idx].Tag;
        _settingsRepo.SaveLanguage(tag);
        AppResources.Culture = tag == null ? null : new CultureInfo(tag);

        // Refresh this page's labels immediately in the new language
        _suppress = true;
        PageTitleText.Text     = AppResources.SettingsTitle;
        ThemeLabelText.Text    = AppResources.ThemeLabel;
        LanguageLabelText.Text = AppResources.LanguageLabel;

        ThemePicker.Items[0] = AppResources.ThemeSystem;
        ThemePicker.Items[1] = AppResources.ThemeLight;
        ThemePicker.Items[2] = AppResources.ThemeDark;
        _suppress = false;
    }
}
