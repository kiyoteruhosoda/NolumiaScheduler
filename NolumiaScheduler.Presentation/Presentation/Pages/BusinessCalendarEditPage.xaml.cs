using NolumiaScheduler.Domain.ValueObjects;
using NolumiaScheduler.Presentation.ViewModels;
using NolumiaScheduler.Resources.Strings;

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

        vm.ReplaceHolidayRequested += OnReplaceHolidayRequested;
    }

    private async void OnReplaceHolidayRequested(LocalDateValue date, string? newName, HolidayDisplayItem existing)
    {
        var message = string.IsNullOrEmpty(existing.Name)
            ? existing.FormattedDate
            : $"{existing.FormattedDate}  {existing.Name}";

        var replace = await DisplayAlertAsync(
            AppResources.HolidaysLabel,
            string.Format(AppResources.ReplaceHolidayMessage, message),
            AppResources.ReplaceButton,
            AppResources.CancelButton);

        if (replace)
            _vm.ReplaceHoliday(date, newName);
    }

    private void OnAddHolidayClicked(object? sender, EventArgs e)
    {
        // Capture the text first, then unfocus (unfocusing may clear the binding)
        var name = HolidayNameEntry.Text ?? "";
        _vm.NewHolidayName = name;
        HolidayNameEntry.Unfocus();
        _vm.AddHolidayCommand.Execute(null);
    }
}
