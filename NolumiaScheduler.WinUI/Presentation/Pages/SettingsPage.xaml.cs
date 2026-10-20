using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using NolumiaScheduler.Domain.Repositories;
using NolumiaScheduler.Domain.ValueObjects;
using NolumiaScheduler.Infrastructure;
using NolumiaScheduler.Infrastructure.Server;
using NolumiaScheduler.Presentation.Resources.Strings;
using NolumiaScheduler.WinUI.Diagnostics;
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

        // Storage backend (which persistence the app is actually running on) + data folder
        StorageLabelText.Text = AppResources.SettingsStorageLabel;
        StorageValueText.Text = StorageBackendDisplayName(App.Services.GetRequiredService<ActiveStorageBackend>().Backend);
        OpenDataFolderButton.Content = AppResources.SettingsOpenDataFolderLink;

        ApplyDiagnosticsTexts();
        ApplyServerTexts();
        ApplyUpdateTexts();

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
        StorageLabelText.Text  = AppResources.SettingsStorageLabel;
        OpenDataFolderButton.Content = AppResources.SettingsOpenDataFolderLink;
        ApplyDiagnosticsTexts();
        ApplyServerTexts();
        ApplyUpdateTexts();

        ThemePicker.Items[0] = AppResources.ThemeSystem;
        ThemePicker.Items[1] = AppResources.ThemeLight;
        ThemePicker.Items[2] = AppResources.ThemeDark;

        StartupViewPicker.Items[0] = AppResources.MonthViewLabel;
        StartupViewPicker.Items[1] = AppResources.WeekViewLabel;
        StartupViewPicker.Items[2] = AppResources.WeekdaysViewLabel;
        _suppress = false;
    }

    private static string StorageBackendDisplayName(StorageBackend backend)
        => backend == StorageBackend.Sqlite ? "SQLite" : "JSON";

    private void OnOpenDataFolderClick(object sender, RoutedEventArgs e)
    {
        var dir = App.Services.GetRequiredService<StorageContext>().DataDirectory;
        Directory.CreateDirectory(dir);
        // Unpackaged WinUI app: shell-execute the folder path to open it in Explorer.
        Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
    }

    private void OnOpenLogFolderClick(object sender, RoutedEventArgs e)
    {
        var dir = AppDiagnostics.LogDirectory;
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
    }

    /// <summary>
    /// Fills in the Scheduler server section. The decisions (sending, moving, reading) live in
    /// Infrastructure.Server; this page only shows the state and offers the buttons.
    /// </summary>
    private void ApplyServerTexts()
    {
        var server = App.Services.GetRequiredService<ServerConnection>();
        var marker = ServerEventMigrator.ReadMarker(server.DataDirectory);
        var signedIn = server.SignIn.IsSignedIn;

        ServerLabelText.Text = AppResources.SettingsServerLabel;
        ServerModeText.Text = marker is null
            ? AppResources.SettingsServerModeLocal
            : string.Format(AppResources.FormatCulture, AppResources.SettingsServerModeServer,
                server.Settings.BaseUri.Host, marker.MigratedAt.LocalDateTime);
        ServerAccountText.Text = !signedIn
            ? AppResources.SettingsServerSignedOut
            : server.SignIn.UserName is { Length: > 0 } name
                ? string.Format(AppResources.FormatCulture, AppResources.SettingsServerSignedIn, name)
                : AppResources.SettingsServerSignedInNoName;

        SignInButton.Content = AppResources.SettingsSignInButton;
        SignOutButton.Content = AppResources.SettingsSignOutButton;
        MigrateButton.Content = AppResources.SettingsMigrateButton;
        SignInButton.Visibility = signedIn ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
        SignOutButton.Visibility = signedIn ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        MigrateButton.Visibility = signedIn && marker is null
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    /// <summary>Offers the downloaded version, if any (task #551). It is applied on exit anyway.</summary>
    private void ApplyUpdateTexts()
    {
        var ready = App.Services.GetRequiredService<AppUpdateHost>().ReadyVersion;
        var visibility = ready is null ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
        UpdateReadyText.Text = ready is null
            ? ""
            : string.Format(AppResources.FormatCulture, AppResources.SettingsUpdateReady, ready);
        UpdateNowButton.Content = AppResources.SettingsUpdateNowButton;
        UpdateReadyText.Visibility = visibility;
        UpdateNowButton.Visibility = visibility;
    }

    private void OnUpdateNowClick(object sender, RoutedEventArgs e)
    {
        UpdateNowButton.IsEnabled = false;
        App.Services.GetRequiredService<AppUpdateHost>().ApplyAndRestart();
    }

    private async void OnSignInClick(object sender, RoutedEventArgs e)
    {
        var server = App.Services.GetRequiredService<ServerConnection>();
        SignInButton.IsEnabled = false;
        ServerResultText.Text = AppResources.SettingsSigningIn;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            await server.SignIn.SignInAsync(
                uri => Process.Start(new ProcessStartInfo { FileName = uri.AbsoluteUri, UseShellExecute = true }),
                timeout.Token);
            ServerResultText.Text = "";
        }
        catch (Exception ex)
        {
            ServerResultText.Text = string.Format(AppResources.FormatCulture, AppResources.SettingsServerError, ex.Message);
        }
        finally
        {
            SignInButton.IsEnabled = true;
            ApplyServerTexts();
        }
    }

    private void OnSignOutClick(object sender, RoutedEventArgs e)
    {
        App.Services.GetRequiredService<ServerConnection>().SignIn.SignOut();
        ServerResultText.Text = "";
        ApplyServerTexts();
    }

    private async void OnMigrateClick(object sender, RoutedEventArgs e)
    {
        var server = App.Services.GetRequiredService<ServerConnection>();
        MigrateButton.IsEnabled = false;
        ServerResultText.Text = AppResources.SettingsMigrating;
        try
        {
            var report = await App.Services.GetRequiredService<ServerEventMigrator>().MigrateAsync(
                App.Services.GetRequiredService<ICalendarEventRepository>(), server.DataDirectory, CancellationToken.None);
            ServerResultText.Text = string.Format(AppResources.FormatCulture, AppResources.SettingsMigrateDone,
                report.Sent, report.Created, report.AlreadyOnServer);
        }
        catch (Exception ex)
        {
            ServerResultText.Text = string.Format(AppResources.FormatCulture, AppResources.SettingsServerError, ex.Message);
        }
        finally
        {
            MigrateButton.IsEnabled = true;
            ApplyServerTexts();
        }
    }

    /// <summary>
    /// Fills in the diagnostics section, including how the previous run ended. Surfacing that in
    /// the UI is the point: an unexpected termination is otherwise invisible to the user, who
    /// only notices that the app "was gone" some time later.
    /// </summary>
    private void ApplyDiagnosticsTexts()
    {
        DiagnosticsLabelText.Text = AppResources.SettingsDiagnosticsLabel;
        DiagnosticsDescriptionText.Text = AppResources.SettingsDiagnosticsDescription;
        OpenLogFolderButton.Content = AppResources.SettingsOpenLogFolderLink;

        LastSessionText.Text = AppDiagnostics.Session?.PreviousSession switch
        {
            null => AppResources.SettingsLastSessionUnknown,
            { CleanExit: true } => AppResources.SettingsLastSessionClean,
            // The machine went down with the app asleep — say so, rather than accusing the app
            // of a crash the user would then go looking for.
            { Suspended: true } previous => string.Format(
                AppResources.FormatCulture,
                AppResources.SettingsLastSessionSuspended,
                previous.LastHeartbeat.LocalDateTime),
            { } previous => string.Format(
                AppResources.FormatCulture,
                AppResources.SettingsLastSessionCrashed,
                previous.LastHeartbeat.LocalDateTime,
                previous.LastEvent),
        };
    }
}
