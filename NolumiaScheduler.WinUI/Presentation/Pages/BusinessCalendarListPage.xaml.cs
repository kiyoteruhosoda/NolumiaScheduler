using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using NolumiaScheduler.Presentation.ViewModels;
using NolumiaScheduler.Presentation.Resources.Strings;
using NolumiaScheduler.WinUI.Helpers;

namespace NolumiaScheduler.Presentation.Pages;

public sealed partial class BusinessCalendarListPage : Page
{
    private BusinessCalendarListViewModel? _vm;

    public BusinessCalendarListPage()
    {
        InitializeComponent();
        EmptyLabel.Text = AppResources.NoCalendarsLabel;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _vm = NolumiaScheduler.WinUI.App.Services.GetRequiredService<BusinessCalendarListViewModel>();
        _vm.Reload();
        RefreshList();
        _vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(BusinessCalendarListViewModel.Calendars))
                RefreshList();
        };
    }

    private void RefreshList()
    {
        CalendarList.ItemsSource = _vm?.Calendars;
        EmptyLabel.Visibility = (_vm?.Calendars?.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnCalendarItemTapped(object sender, TappedRoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is BusinessCalendarSummary item)
            NavigationService.Instance.Navigate(typeof(BusinessCalendarEditPage),
                new BusinessCalendarEditParams(CalendarId: item.Id));
    }

    private void OnAddCalendarClicked(object sender, RoutedEventArgs e)
        => NavigationService.Instance.Navigate(typeof(BusinessCalendarEditPage));
}
