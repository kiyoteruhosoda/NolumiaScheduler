using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using NolumiaScheduler.Application.Services;
using NolumiaScheduler.Domain.Services;
using NolumiaScheduler.Presentation.Resources.Strings;
using NolumiaScheduler.Presentation.Services;
using NolumiaScheduler.WinUI.Helpers;
using NolumiaScheduler.WinUI.Presentation.Pages;
using System.ComponentModel;

namespace NolumiaScheduler.WinUI;

public sealed partial class MainWindow : Window, INotifyPropertyChanged
{
    private bool _canGoBack;
    public bool CanGoBack
    {
        get => _canGoBack;
        private set
        {
            _canGoBack = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanGoBack)));
        }
    }

    private bool _suppressNavChange;

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainWindow()
    {
        InitializeComponent();

        // Apply the Mica backdrop from code-behind. Setting Window.SystemBackdrop
        // via XAML throws a generic XamlParseException at InitializeComponent in
        // self-contained / unpackaged Release builds, so configure it here where
        // the type is activated through the normal CLR path. Guarded so machines
        // without Mica support simply keep the default window background.
        if (Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported())
        {
            SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
        }

        // Localized nav item labels
        MonthNavItem.Content = AppResources.MonthViewLabel;
        WeekNavItem.Content  = AppResources.WeekViewLabel;
        BusinessCalendarNavItem.Content = AppResources.BusinessCalendarsTab;

        NavigationService.Instance.Initialize(ContentFrame);

        ContentFrame.Navigated += OnFrameNavigated;

        // Default to week view
        NavView.SelectedItem = WeekNavItem;
        ContentFrame.Navigate(typeof(CalendarPage), "Week");

        // Intercept close to minimize to tray instead
        AppWindow.Closing += OnAppWindowClosing;

        // Intercept minimize to hide to tray
        ((Microsoft.UI.Windowing.OverlappedPresenter)AppWindow.Presenter).IsMinimizable = true;
        AppWindow.Changed += OnAppWindowChanged;
    }

    private bool _wasMinimized;

    private void OnAppWindowChanged(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowChangedEventArgs args)
    {
        if (!args.DidPresenterChange && !args.DidSizeChange) return;

        var presenter = (Microsoft.UI.Windowing.OverlappedPresenter)sender.Presenter;
        if (presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Minimized && !_wasMinimized)
        {
            _wasMinimized = true;
            sender.Hide();
            MinimizedToTray?.Invoke(this, EventArgs.Empty);
        }
        else if (presenter.State != Microsoft.UI.Windowing.OverlappedPresenterState.Minimized)
        {
            _wasMinimized = false;
        }
    }

    private bool _forceClose;

    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    private void OnAppWindowClosing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        if (_forceClose) return;
        args.Cancel = true;
        this.AppWindow.Hide();
        MinimizedToTray?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised when the window is hidden to tray (close or minimize).</summary>
    public event EventHandler? MinimizedToTray;
    public event EventHandler? RestoredFromTray;

    private void OnFrameNavigated(object sender, NavigationEventArgs e)
    {
        CanGoBack = ContentFrame.CanGoBack;

        _suppressNavChange = true;
        if (e.SourcePageType == typeof(CalendarPage))
            NavView.SelectedItem = e.Parameter as string == "Month" ? MonthNavItem : WeekNavItem;
        else if (e.SourcePageType == typeof(BusinessCalendarListPage))
            NavView.SelectedItem = BusinessCalendarNavItem;
        _suppressNavChange = false;
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_suppressNavChange || args.IsSettingsSelected) return;

        if (args.SelectedItem is NavigationViewItem item)
        {
            switch (item.Tag as string)
            {
                case "Month":
                    ContentFrame.Navigate(typeof(CalendarPage), "Month");
                    break;
                case "Week":
                    ContentFrame.Navigate(typeof(CalendarPage), "Week");
                    break;
                case "BusinessCalendars":
                    if (ContentFrame.CurrentSourcePageType != typeof(BusinessCalendarListPage))
                        ContentFrame.Navigate(typeof(BusinessCalendarListPage));
                    break;
                case "Debug":
                    OpenAlarmDebugWindow();
                    break;
            }
        }
    }

    private AlarmDebugWindow? _debugWindow;

    private void OpenAlarmDebugWindow()
    {
        if (_debugWindow is null || _debugWindow.AppWindow is null)
        {
            var services = App.Services;
            _debugWindow = new AlarmDebugWindow(
                services.GetRequiredService<IAlarmService>(),
                services.GetRequiredService<CalendarEventApplicationService>(),
                services.GetRequiredService<IOccurrenceExpander>(),
                services.GetRequiredService<TimeProvider>());
        }
        _debugWindow.Activate();
    }

    private void OnBackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
        => NavigationService.Instance.GoBack();

    private void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        => throw e.Exception;
}
