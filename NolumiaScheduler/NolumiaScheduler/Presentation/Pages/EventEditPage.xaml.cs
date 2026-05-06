using NolumiaScheduler.Presentation.ViewModels;

namespace NolumiaScheduler.Presentation.Pages;

[QueryProperty(nameof(EventId), "eventId")]
[QueryProperty(nameof(StartDate), "startDate")]
[QueryProperty(nameof(StartMinute), "startMinute")]
public partial class EventEditPage : ContentPage
{
    private readonly EventEditViewModel _vm;

    private string? _eventId;
    private string? _startDateRaw;
    private string? _startMinuteRaw;

    public string? EventId { set { _eventId = value; ApplyNavigationContext(); } }
    public string? StartDate { set { _startDateRaw = value; ApplyNavigationContext(); } }
    public string? StartMinute { set { _startMinuteRaw = value; ApplyNavigationContext(); } }

    public EventEditPage(EventEditViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;

        vm.SaveCompleted += async () => await Shell.Current.GoToAsync("..");
    }

    private void ApplyNavigationContext()
    {
        if (!string.IsNullOrWhiteSpace(_eventId))
        {
            _vm.LoadEvent(_eventId);
            return;
        }

        if (DateOnly.TryParse(_startDateRaw, out var date))
        {
            var minute = int.TryParse(_startMinuteRaw, out var parsedMinute) ? parsedMinute : 9 * 60;
            _vm.InitializeNewEvent(date, minute);
        }
    }
}
