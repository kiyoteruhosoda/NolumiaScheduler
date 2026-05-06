using NolumiaScheduler.Presentation.ViewModels;

namespace NolumiaScheduler.Presentation.Pages;

[QueryProperty(nameof(EventId), "eventId")]
[QueryProperty(nameof(StartDate), "startDate")]
[QueryProperty(nameof(StartMinute), "startMinute")]
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


    public string? StartDate
    {
        set
        {
            if (DateOnly.TryParse(value, out var date))
                _vm.ApplyInitialStart(date, _pendingStartMinute);
        }
    }

    private int? _pendingStartMinute;
    public string? StartMinute
    {
        set
        {
            if (int.TryParse(value, out var minute))
                _pendingStartMinute = minute;
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
