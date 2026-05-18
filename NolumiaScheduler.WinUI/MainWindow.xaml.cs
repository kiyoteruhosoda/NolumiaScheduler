using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using NolumiaScheduler.Presentation.Pages;
using NolumiaScheduler.Resources.Strings;
using NolumiaScheduler.WinUI.Helpers;
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

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainWindow()
    {
        InitializeComponent();

        // Localized nav item labels
        CalendarNavItem.Content         = AppResources.CalendarTab;
        BusinessCalendarNavItem.Content = AppResources.BusinessCalendarsTab;

        NavigationService.Instance.Initialize(ContentFrame);

        ContentFrame.Navigated += OnFrameNavigated;

        // Default to calendar
        NavView.SelectedItem = CalendarNavItem;
        ContentFrame.Navigate(typeof(CalendarPage));
    }

    private void OnFrameNavigated(object sender, NavigationEventArgs e)
    {
        CanGoBack = ContentFrame.CanGoBack;

        // Sync nav selection with current page type
        if (e.SourcePageType == typeof(CalendarPage))
            NavView.SelectedItem = CalendarNavItem;
        else if (e.SourcePageType == typeof(BusinessCalendarListPage))
            NavView.SelectedItem = BusinessCalendarNavItem;
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected) return;

        if (args.SelectedItem is NavigationViewItem item)
        {
            switch (item.Tag as string)
            {
                case "Calendar":
                    if (ContentFrame.CurrentSourcePageType != typeof(CalendarPage))
                        ContentFrame.Navigate(typeof(CalendarPage));
                    break;
                case "BusinessCalendars":
                    if (ContentFrame.CurrentSourcePageType != typeof(BusinessCalendarListPage))
                        ContentFrame.Navigate(typeof(BusinessCalendarListPage));
                    break;
            }
        }
    }

    private void OnBackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
        => NavigationService.Instance.GoBack();

    private void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        => throw e.Exception;
}
