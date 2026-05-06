using NolumiaScheduler.Presentation.ViewModels;

namespace NolumiaScheduler.Presentation.Pages;

[QueryProperty(nameof(CalendarId), "calendarId")]
public partial class BusinessCalendarEditPage : ContentPage
{
    private readonly BusinessCalendarEditViewModel _vm;

    public string? CalendarId
    {
        set => _vm.CalendarId = string.IsNullOrEmpty(value) ? null : value;
    }

    public BusinessCalendarEditPage(BusinessCalendarEditViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;

        vm.SaveCompleted   += async () => await Shell.Current.GoToAsync("..");
        vm.DeleteCompleted += async () => await Shell.Current.GoToAsync("..");
    }
}
