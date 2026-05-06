using NolumiaScheduler.Presentation.ViewModels;

namespace NolumiaScheduler.Presentation.Pages;

[QueryProperty(nameof(EventId), "eventId")]
public partial class EventEditPage : ContentPage
{
    private readonly EventEditViewModel _vm;

    public string? EventId
    {
        set
        {
            if (!string.IsNullOrEmpty(value))
                _vm.LoadEvent(value);
        }
    }

    public EventEditPage(EventEditViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;

        vm.SaveCompleted += async () => await Shell.Current.GoToAsync("..");
    }
}
