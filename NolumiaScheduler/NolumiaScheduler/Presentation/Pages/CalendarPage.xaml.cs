using NolumiaScheduler.Presentation.ViewModels;

namespace NolumiaScheduler.Presentation.Pages;

public partial class CalendarPage : ContentPage
{
    private readonly CalendarViewModel _vm;

    public CalendarPage(CalendarViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    private void OnDayCellSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is CalendarDayCell cell)
        {
            _vm.SelectDay(cell);
            ((CollectionView)sender).SelectedItem = null;
        }
    }
}
