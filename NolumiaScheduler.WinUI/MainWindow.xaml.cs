using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
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
        MonthNavItem.Content        = AppResources.MonthViewLabel;
        WeekNavItem.Content         = AppResources.WeekViewLabel;
        WeekdaysNavItem.Content     = AppResources.WeekdaysViewLabel;
        BusinessCalendarNavItem.Content = AppResources.BusinessCalendarsTab;

        NavigationService.Instance.Initialize(ContentFrame);

        // The pane toggle button lives inside the NavigationView template and is only
        // reachable after Loaded; refresh the icon on every open/close so it always
        // points in the direction the click will move the pane.
        // Run with Low priority so WinUI's own VisualState transitions (which would
        // reset Content) are already applied before we override the icon.
        NavView.Loaded += (_, _) => EnqueuePaneToggleIcon(NavView.IsPaneOpen);
        // IsPaneOpen still holds the old value inside these events; pass the target state.
        NavView.PaneOpening += (_, _) => EnqueuePaneToggleIcon(paneOpen: true);
        NavView.PaneClosing += (_, _) => EnqueuePaneToggleIcon(paneOpen: false);

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
            NavView.SelectedItem = e.Parameter as string switch
            {
                "Month"    => MonthNavItem,
                "Weekdays" => WeekdaysNavItem,
                _          => WeekNavItem,
            };
        else if (e.SourcePageType == typeof(BusinessCalendarListPage))
            NavView.SelectedItem = BusinessCalendarNavItem;
        _suppressNavChange = false;
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_suppressNavChange) return;

        if (args.IsSettingsSelected)
        {
            ContentFrame.Navigate(typeof(SettingsPage));
            return;
        }

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
                case "Weekdays":
                    ContentFrame.Navigate(typeof(CalendarPage), "Weekdays");
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

    // The pane toggle (default hamburger ☰, tooltip "Close Navigation" when open) does not
    // read as open/close. Replace its icon with a direction that says what the click will do:
    // ForwardSolidBold (F8AD) when the pane is closed, BackSolidBold (F8AC) when it is open.
    private void EnqueuePaneToggleIcon(bool paneOpen)
        // DispatcherQueuePriority.Low ensures this runs after WinUI's VisualState
        // transitions, which otherwise reset ContentTemplate/Content on the button.
        => DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => ApplyPaneToggleIcon(paneOpen));

    private void ApplyPaneToggleIcon(bool paneOpen)
    {
        if (FindDescendant<Button>(NavView, b => b.Name == "TogglePaneButton") is not { } btn)
            return;
        // Clear the template so it cannot restore the original hamburger icon on
        // the next layout pass, then set our directional icon directly as Content.
        btn.ContentTemplate = null;
        btn.Content = new FontIcon
        {
            Glyph = paneOpen ? "\uF8AC" : "\uF8AD",
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"),
            FontSize = 14
        };
    }

    private static T? FindDescendant<T>(DependencyObject parent, Func<T, bool> predicate)
        where T : DependencyObject
    {
        var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T t && predicate(t)) return t;
            var found = FindDescendant(child, predicate);
            if (found != null) return found;
        }
        return null;
    }
}
