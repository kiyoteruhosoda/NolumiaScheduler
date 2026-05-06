using NolumiaScheduler.Presentation.ViewModels;

namespace NolumiaScheduler.Presentation.Pages;

public partial class EventEditPage : ContentPage
{
    private readonly EventEditViewModel _vm;

    public EventEditPage(EventEditViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;

        vm.SaveCompleted += async () => await Shell.Current.GoToAsync("..");
    }
}
