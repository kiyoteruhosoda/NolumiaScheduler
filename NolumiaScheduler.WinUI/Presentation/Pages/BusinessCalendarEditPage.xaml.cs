using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using NolumiaScheduler.Domain.ValueObjects;
using NolumiaScheduler.Presentation.Resources.Strings;
using NolumiaScheduler.Presentation.ViewModels;
using NolumiaScheduler.WinUI.Helpers;

namespace NolumiaScheduler.WinUI.Presentation.Pages;

public sealed partial class BusinessCalendarEditPage : Page
{
    private BusinessCalendarEditViewModel? _vm;

    public BusinessCalendarEditPage()
    {
        InitializeComponent();

        NameLabel.Text = AppResources.CalendarNamePlaceholder;
        WorkdaysLabel.Text = AppResources.WorkdaysLabel;
        HolidaysLabel.Text = AppResources.HolidaysLabel;
        AddHolidayBtn.Content = AppResources.AddHolidayButton;
        SaveBtn.Content = AppResources.SaveButton;
        DeleteBtn.Content = AppResources.DeleteCalendarButton;
        LblSun.Text = AppResources.DaySun;
        LblMon.Text = AppResources.DayMon;
        LblTue.Text = AppResources.DayTue;
        LblWed.Text = AppResources.DayWed;
        LblThu.Text = AppResources.DayThu;
        LblFri.Text = AppResources.DayFri;
        LblSat.Text = AppResources.DaySat;
        NameBox.PlaceholderText = AppResources.CalendarNamePlaceholder;
        NewHolidayNameBox.PlaceholderText = AppResources.HolidayNamePlaceholder;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _vm = NolumiaScheduler.WinUI.App.Services.GetRequiredService<BusinessCalendarEditViewModel>();

        if (e.Parameter is BusinessCalendarEditParams p && p.CalendarId != null)
            _vm.CalendarId = p.CalendarId;

        _vm.SaveCompleted += () => NavigationService.Instance.GoBack();
        _vm.DeleteCompleted += () => NavigationService.Instance.GoBack();
        _vm.ReplaceHolidayRequested += OnReplaceHolidayRequested;

        BindViewModel();
    }

    private void BindViewModel()
    {
        if (_vm == null) return;

        NameBox.Text = _vm.Name;
        NameBox.TextChanged += (_, _) => _vm.Name = NameBox.Text;

        ChkSun.IsChecked = _vm.WorkSunday; ChkSun.Checked += (_, _) => _vm.WorkSunday = true; ChkSun.Unchecked += (_, _) => _vm.WorkSunday = false;
        ChkMon.IsChecked = _vm.WorkMonday; ChkMon.Checked += (_, _) => _vm.WorkMonday = true; ChkMon.Unchecked += (_, _) => _vm.WorkMonday = false;
        ChkTue.IsChecked = _vm.WorkTuesday; ChkTue.Checked += (_, _) => _vm.WorkTuesday = true; ChkTue.Unchecked += (_, _) => _vm.WorkTuesday = false;
        ChkWed.IsChecked = _vm.WorkWednesday; ChkWed.Checked += (_, _) => _vm.WorkWednesday = true; ChkWed.Unchecked += (_, _) => _vm.WorkWednesday = false;
        ChkThu.IsChecked = _vm.WorkThursday; ChkThu.Checked += (_, _) => _vm.WorkThursday = true; ChkThu.Unchecked += (_, _) => _vm.WorkThursday = false;
        ChkFri.IsChecked = _vm.WorkFriday; ChkFri.Checked += (_, _) => _vm.WorkFriday = true; ChkFri.Unchecked += (_, _) => _vm.WorkFriday = false;
        ChkSat.IsChecked = _vm.WorkSaturday; ChkSat.Checked += (_, _) => _vm.WorkSaturday = true; ChkSat.Unchecked += (_, _) => _vm.WorkSaturday = false;

        HolidayList.ItemsSource = _vm.Holidays;
        DeleteBtn.Visibility = _vm.IsEditing ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

        _vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(BusinessCalendarEditViewModel.Holidays))
                HolidayList.ItemsSource = _vm.Holidays;
            if (args.PropertyName == nameof(BusinessCalendarEditViewModel.IsEditing))
                DeleteBtn.Visibility = _vm.IsEditing ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        };
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
        => _vm?.SaveCommand.Execute(null);

    private void OnDeleteClicked(object sender, RoutedEventArgs e)
        => _vm?.DeleteCommand.Execute(null);

    private void OnAddHolidayClicked(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        if (NewHolidayDatePicker.Date is { } d)
            _vm.NewHolidayDate = new DateTime(d.Year, d.Month, d.Day);
        _vm.NewHolidayName = NewHolidayNameBox.Text ?? "";
        NewHolidayNameBox.Focus(FocusState.Programmatic);
        _vm.AddHolidayCommand.Execute(null);
        NewHolidayNameBox.Text = "";
    }

    private async void OnReplaceHolidayRequested(LocalDateValue date, string? newName, HolidayDisplayItem existing)
    {
        var message = string.IsNullOrEmpty(existing.Name)
            ? existing.FormattedDate
            : $"{existing.FormattedDate}  {existing.Name}";

        var dialog = new ContentDialog
        {
            Title = AppResources.HolidaysLabel,
            Content = string.Format(AppResources.ReplaceHolidayMessage, message),
            PrimaryButtonText = AppResources.ReplaceButton,
            CloseButtonText = AppResources.CancelButton,
            XamlRoot = XamlRoot
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
            _vm?.ReplaceHoliday(date, newName);
    }
}
