using NolumiaScheduler.Presentation.ViewModels;

namespace NolumiaScheduler.Presentation.Pages;

public partial class BusinessCalendarListPage : ContentPage
{
    private readonly BusinessCalendarListViewModel _vm;

    public BusinessCalendarListPage(BusinessCalendarListViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.Reload();
    }

    private async void OnCalendarSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is BusinessCalendarSummary item)
        {
            ((CollectionView)sender).SelectedItem = null;
            await Shell.Current.GoToAsync($"BusinessCalendarEdit?calendarId={item.Id}");
        }
    }

    private async void OnAddCalendarClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("BusinessCalendarEdit");
    }
}
