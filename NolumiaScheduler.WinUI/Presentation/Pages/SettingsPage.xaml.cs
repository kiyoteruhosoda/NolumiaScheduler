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

    // Stored tags in fixed order matching StartupViewPicker indices; "Week" is the
    // application default (see MainWindow's startup navigation).
    private static readonly string[] StartupViewOptions = ["Month", "Week", "Weekdays"];

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
        StartupViewLabelText.Text = AppResources.StartupViewLabel;
        VersionLabelText.Text = AppResources.SettingsVersionLabel;
        VersionValueText.Text = Helpers.AppVersion.Display;

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

        // Startup view picker
        StartupViewPicker.Items.Clear();
        StartupViewPicker.Items.Add(AppResources.MonthViewLabel);
        StartupViewPicker.Items.Add(AppResources.WeekViewLabel);
        StartupViewPicker.Items.Add(AppResources.WeekdaysViewLabel);
        var savedView = _settingsRepo.GetStartupView();
        var viewIdx = Array.IndexOf(StartupViewOptions, savedView);
        StartupViewPicker.SelectedIndex = viewIdx >= 0 ? viewIdx : Array.IndexOf(StartupViewOptions, "Week");

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

    private void OnStartupViewSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || _settingsRepo == null) return;

        var idx = StartupViewPicker.SelectedIndex;
        if (idx < 0 || idx >= StartupViewOptions.Length) return;

        _settingsRepo.SaveStartupView(StartupViewOptions[idx]);
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
        StartupViewLabelText.Text = AppResources.StartupViewLabel;
        VersionLabelText.Text  = AppResources.SettingsVersionLabel;

        ThemePicker.Items[0] = AppResources.ThemeSystem;
        ThemePicker.Items[1] = AppResources.ThemeLight;
        ThemePicker.Items[2] = AppResources.ThemeDark;

        StartupViewPicker.Items[0] = AppResources.MonthViewLabel;
        StartupViewPicker.Items[1] = AppResources.WeekViewLabel;
        StartupViewPicker.Items[2] = AppResources.WeekdaysViewLabel;
        _suppress = false;
    }
}
