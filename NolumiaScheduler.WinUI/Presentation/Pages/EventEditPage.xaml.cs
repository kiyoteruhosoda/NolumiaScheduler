using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using NolumiaScheduler.Presentation.ViewModels;
using NolumiaScheduler.Resources.Strings;
using NolumiaScheduler.WinUI.Helpers;

namespace NolumiaScheduler.Presentation.Pages;

public sealed partial class EventEditPage : Page
{
    private EventEditViewModel? _vm;

    // Guards to prevent feedback loops when programmatically updating controls
    private bool _suppressStartTimeChanged;
    private bool _suppressEndTimeChanged;
    private bool _suppressRepeatTypeChanged;
    private bool _suppressMonthlyRuleChanged;
    private bool _suppressYearlyRuleChanged;
    private bool _suppressIntervalChanged;
    private bool _suppressDomChanged;
    private bool _suppressYearlyMonthChanged;
    private bool _suppressYearlyDayChanged;
    private bool _suppressYearlyMonth2Changed;
    private bool _suppressStartDateChanged;
    private bool _suppressEndDateChanged;
    private bool _suppressWeekIndexChanged;
    private bool _suppressMonthlyWeekdayChanged;
    private bool _suppressYearlyWeekIndexChanged;
    private bool _suppressYearlyWeekdayChanged;
    private bool _suppressAdjustmentChanged;
    private bool _suppressCalendarPickerChanged;
    private bool _suppressStartTimePickerChanged;
    private bool _suppressEndTimePickerChanged;

    // 15-minute interval time items ("00:00" to "23:45")
    private static readonly List<string> TimeItems = [.. Enumerable.Range(0, 96).Select(i => $"{i / 4:D2}:{(i % 4) * 15:D2}")];

    public EventEditPage()
    {
        InitializeComponent();

        // Static string labels
        TitleBox.PlaceholderText    = AppResources.EventTitlePlaceholder;
        LocationBox.PlaceholderText = AppResources.EventLocationPlaceholder;
        AllDayLabel.Text            = AppResources.AllDay;
        StartLabel.Text             = AppResources.StartLabel;
        StartTimeLabel.Text         = AppResources.StartLabel;
        EndLabel.Text               = AppResources.EndLabel;
        RepeatLabel.Text            = AppResources.RepeatLabel;
        WeekdaysLabel.Text          = AppResources.WeekdaysLabel;
        WLblSun.Text = AppResources.DaySun;
        WLblMon.Text = AppResources.DayMon;
        WLblTue.Text = AppResources.DayTue;
        WLblWed.Text = AppResources.DayWed;
        WLblThu.Text = AppResources.DayThu;
        WLblFri.Text = AppResources.DayFri;
        WLblSat.Text = AppResources.DaySat;
        DayLbl.Text             = AppResources.DayLabel;
        IntervalLabel.Text      = AppResources.IntervalLabel;
        EndDateLabel.Text       = AppResources.EndDateLabel;
        AdjustmentLabel.Text    = AppResources.AdjustmentLabel;
        BusinessCalendarLabel.Text = AppResources.BusinessCalendarLabel;
        AlarmLabel.Text         = AppResources.AlarmLabel;
        AlarmEnableLabel.Text   = AppResources.AlarmEnable;
        Notify15Label.Text      = AppResources.AlarmNotify15Min;
        Notify5Label.Text       = AppResources.AlarmNotify5Min;
        Notify1Label.Text       = AppResources.AlarmNotify1Min;
        SaveBtn.Content         = AppResources.SaveButton;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _vm = NolumiaScheduler.WinUI.App.Services.GetRequiredService<EventEditViewModel>();

        if (e.Parameter is EventEditParams p)
        {
            if (p.EventId != null)
            {
                NolumiaScheduler.Domain.ValueObjects.OccurrenceLocalKey? occKey = null;
                if (p.OccurrenceDate != null && p.OccurrenceStartMinute.HasValue
                    && DateTime.TryParse(p.OccurrenceDate, out var occDt))
                {
                    var occDate = NolumiaScheduler.Domain.ValueObjects.LocalDateValue.FromDateOnly(DateOnly.FromDateTime(occDt));
                    var occTime = new NolumiaScheduler.Domain.ValueObjects.LocalTimeValue(
                        p.OccurrenceStartMinute.Value / 60,
                        p.OccurrenceStartMinute.Value % 60, 0);
                    occKey = new NolumiaScheduler.Domain.ValueObjects.OccurrenceLocalKey(occDate, occTime);
                }
                _vm.LoadEvent(p.EventId, occKey);
            }
            else if (p.StartDate != null && p.StartMinute.HasValue
                     && DateTime.TryParse(p.StartDate, out var startDt))
            {
                var dateOnly = DateOnly.FromDateTime(startDt);
                _vm.InitializeNewEvent(dateOnly, p.StartMinute.Value);
            }
        }

        _vm.SaveCompleted += () => NavigationService.Instance.GoBack();
        _vm.PropertyChanged += OnVmPropertyChanged;

        BindViewModel();
    }

    private void BindViewModel()
    {
        if (_vm == null) return;

        // Title / Location
        TitleBox.Text    = _vm.Title;
        LocationBox.Text = _vm.Location;
        TitleBox.TextChanged    += (_, _) => _vm.Title    = TitleBox.Text;
        LocationBox.TextChanged += (_, _) => _vm.Location = LocationBox.Text;

        // All Day
        AllDaySwitch.IsOn = _vm.AllDay;

        // Start date
        _suppressStartDateChanged = true;
        StartDatePicker.Date = new DateTimeOffset(_vm.StartDate);
        _suppressStartDateChanged = false;

        // Time boxes
        StartTimePicker.ItemsSource = TimeItems;
        EndTimePicker.ItemsSource = TimeItems;

        _suppressStartTimeChanged = true;
        _suppressStartTimePickerChanged = true;
        StartTimeBox.Text = _vm.StartTime.ToString(@"hh\:mm");
        SyncTimePickerToValue(StartTimePicker, _vm.StartTime, ref _suppressStartTimePickerChanged);
        _suppressStartTimeChanged = false;
        _suppressStartTimePickerChanged = false;

        _suppressEndTimeChanged = true;
        _suppressEndTimePickerChanged = true;
        EndTimeBox.Text = _vm.EndTime.ToString(@"hh\:mm");
        SyncTimePickerToValue(EndTimePicker, _vm.EndTime, ref _suppressEndTimePickerChanged);
        _suppressEndTimeChanged = false;
        _suppressEndTimePickerChanged = false;

        // Recurrence pickers
        RepeatTypePicker.ItemsSource   = EventEditViewModel.RepeatTypeItems;
        MonthlyRulePicker.ItemsSource  = EventEditViewModel.MonthlyRuleItems;
        YearlyRulePicker.ItemsSource   = EventEditViewModel.YearlyRuleItems;
        WeekIndexPicker.ItemsSource    = EventEditViewModel.WeekIndexItems;
        MonthlyWeekdayPicker.ItemsSource = EventEditViewModel.WeekdayItems;
        YearlyWeekIndexPicker.ItemsSource = EventEditViewModel.WeekIndexItems;
        YearlyWeekdayPicker.ItemsSource   = EventEditViewModel.WeekdayItems;
        AdjustmentPicker.ItemsSource   = EventEditViewModel.AdjustmentItems;

        _suppressRepeatTypeChanged = true;
        RepeatTypePicker.SelectedIndex = _vm.RepeatTypeIndex;
        _suppressRepeatTypeChanged = false;

        _suppressMonthlyRuleChanged = true;
        MonthlyRulePicker.SelectedIndex = _vm.MonthlyRuleIndex;
        _suppressMonthlyRuleChanged = false;

        _suppressYearlyRuleChanged = true;
        YearlyRulePicker.SelectedIndex = _vm.YearlyRuleIndex;
        _suppressYearlyRuleChanged = false;

        // Weekday checkboxes for weekly recurrence
        WChkSun.IsChecked = _vm.WeekSun;
        WChkMon.IsChecked = _vm.WeekMon;
        WChkTue.IsChecked = _vm.WeekTue;
        WChkWed.IsChecked = _vm.WeekWed;
        WChkThu.IsChecked = _vm.WeekThu;
        WChkFri.IsChecked = _vm.WeekFri;
        WChkSat.IsChecked = _vm.WeekSat;

        // Monthly
        _suppressDomChanged = true;
        DomBox.Text = _vm.DayOfMonth.ToString();
        _suppressDomChanged = false;

        _suppressWeekIndexChanged = true;
        WeekIndexPicker.SelectedIndex = _vm.WeekIndexPickerIndex;
        _suppressWeekIndexChanged = false;

        _suppressMonthlyWeekdayChanged = true;
        MonthlyWeekdayPicker.SelectedIndex = _vm.MonthlyWeekdayIndex;
        _suppressMonthlyWeekdayChanged = false;

        // Yearly
        _suppressYearlyMonthChanged = true;
        YearlyMonthBox.Text = _vm.YearlyMonth.ToString();
        _suppressYearlyMonthChanged = false;

        _suppressYearlyDayChanged = true;
        YearlyDayBox.Text = _vm.YearlyDay.ToString();
        _suppressYearlyDayChanged = false;

        _suppressYearlyMonth2Changed = true;
        YearlyMonthBox2.Text = _vm.YearlyMonth.ToString();
        _suppressYearlyMonth2Changed = false;

        _suppressYearlyWeekIndexChanged = true;
        YearlyWeekIndexPicker.SelectedIndex = _vm.YearlyWeekIndexPickerIndex;
        _suppressYearlyWeekIndexChanged = false;

        _suppressYearlyWeekdayChanged = true;
        YearlyWeekdayPicker.SelectedIndex = _vm.YearlyWeekdayIndex;
        _suppressYearlyWeekdayChanged = false;

        // Interval / End Date
        _suppressIntervalChanged = true;
        IntervalBox.Text = _vm.Interval.ToString();
        _suppressIntervalChanged = false;

        IntervalUnitLabel.Text = _vm.IntervalUnitLabel;

        _suppressEndDateChanged = true;
        EndDatePicker.Date = new DateTimeOffset(_vm.EndDate);
        _suppressEndDateChanged = false;

        // Adjustment
        _suppressAdjustmentChanged = true;
        AdjustmentPicker.SelectedIndex = _vm.AdjustmentIndex;
        _suppressAdjustmentChanged = false;

        CalendarPicker.ItemsSource = _vm.AvailableCalendarNames;
        _suppressCalendarPickerChanged = true;
        CalendarPicker.SelectedIndex = _vm.SelectedCalendarIndex;
        _suppressCalendarPickerChanged = false;

        // Alarm
        AlarmSwitch.IsOn       = _vm.AlarmEnabled;
        ChkNotify15.IsChecked  = _vm.AlarmNotify15Min;
        ChkNotify5.IsChecked   = _vm.AlarmNotify5Min;
        ChkNotify1.IsChecked   = _vm.AlarmNotify1Min;

        // Validation
        ValidationBorder.Visibility = _vm.HasValidationError ? Visibility.Visible : Visibility.Collapsed;
        ValidationText.Text = _vm.ValidationError;

        // Apply initial section visibility
        ApplySectionVisibility();
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (_vm == null) return;

        switch (args.PropertyName)
        {
            case nameof(EventEditViewModel.HasValidationError):
            case nameof(EventEditViewModel.ValidationError):
                ValidationBorder.Visibility = _vm.HasValidationError ? Visibility.Visible : Visibility.Collapsed;
                ValidationText.Text = _vm.ValidationError;
                break;

            case nameof(EventEditViewModel.ShowTimeSection):
            case nameof(EventEditViewModel.IsRecurring):
            case nameof(EventEditViewModel.IsWeekly):
            case nameof(EventEditViewModel.IsMonthly):
            case nameof(EventEditViewModel.IsYearly):
            case nameof(EventEditViewModel.IsMonthlyDayOfMonth):
            case nameof(EventEditViewModel.IsMonthlyNthWeekday):
            case nameof(EventEditViewModel.IsYearlyDayOfMonth):
            case nameof(EventEditViewModel.IsYearlyNthWeekday):
            case nameof(EventEditViewModel.HasAdjustment):
            case nameof(EventEditViewModel.HasAvailableCalendars):
            case nameof(EventEditViewModel.ShowAlarmNotifyOptions):
                ApplySectionVisibility();
                break;

            case nameof(EventEditViewModel.IntervalUnitLabel):
                IntervalUnitLabel.Text = _vm.IntervalUnitLabel;
                break;

            case nameof(EventEditViewModel.RepeatTypeIndex):
                if (!_suppressRepeatTypeChanged)
                {
                    _suppressRepeatTypeChanged = true;
                    RepeatTypePicker.SelectedIndex = _vm.RepeatTypeIndex;
                    _suppressRepeatTypeChanged = false;
                }
                break;

            case nameof(EventEditViewModel.StartDate):
                if (!_suppressStartDateChanged)
                {
                    _suppressStartDateChanged = true;
                    StartDatePicker.Date = new DateTimeOffset(_vm.StartDate);
                    _suppressStartDateChanged = false;
                }
                break;

            case nameof(EventEditViewModel.StartTime):
                if (!_suppressStartTimeChanged)
                {
                    _suppressStartTimeChanged = true;
                    StartTimeBox.Text = _vm.StartTime.ToString(@"hh\:mm");
                    _suppressStartTimeChanged = false;
                }
                if (!_suppressStartTimePickerChanged)
                {
                    _suppressStartTimePickerChanged = true;
                    SyncTimePickerToValue(StartTimePicker, _vm.StartTime, ref _suppressStartTimePickerChanged);
                    _suppressStartTimePickerChanged = false;
                }
                break;

            case nameof(EventEditViewModel.EndTime):
                if (!_suppressEndTimeChanged)
                {
                    _suppressEndTimeChanged = true;
                    EndTimeBox.Text = _vm.EndTime.ToString(@"hh\:mm");
                    _suppressEndTimeChanged = false;
                }
                if (!_suppressEndTimePickerChanged)
                {
                    _suppressEndTimePickerChanged = true;
                    SyncTimePickerToValue(EndTimePicker, _vm.EndTime, ref _suppressEndTimePickerChanged);
                    _suppressEndTimePickerChanged = false;
                }
                break;
        }
    }

    private void ApplySectionVisibility()
    {
        if (_vm == null) return;

        TimeSection.Visibility         = _vm.ShowTimeSection      ? Visibility.Visible : Visibility.Collapsed;
        WeeklySection.Visibility       = _vm.IsWeekly             ? Visibility.Visible : Visibility.Collapsed;
        MonthlySection.Visibility      = _vm.IsMonthly            ? Visibility.Visible : Visibility.Collapsed;
        YearlySection.Visibility       = _vm.IsYearly             ? Visibility.Visible : Visibility.Collapsed;
        RecurringSection.Visibility    = _vm.IsRecurring           ? Visibility.Visible : Visibility.Collapsed;
        AdjustmentSection.Visibility   = _vm.IsRecurring           ? Visibility.Visible : Visibility.Collapsed;

        DomSection.Visibility          = _vm.IsMonthlyDayOfMonth  ? Visibility.Visible : Visibility.Collapsed;
        NthWeekdaySection.Visibility   = _vm.IsMonthlyNthWeekday  ? Visibility.Visible : Visibility.Collapsed;
        YearlyDomSection.Visibility    = _vm.IsYearlyDayOfMonth   ? Visibility.Visible : Visibility.Collapsed;
        YearlyNthSection.Visibility    = _vm.IsYearlyNthWeekday   ? Visibility.Visible : Visibility.Collapsed;

        CalendarPickerSection.Visibility = (_vm.HasAdjustment && _vm.HasAvailableCalendars)
            ? Visibility.Visible : Visibility.Collapsed;

        AlarmNotifySection.Visibility  = _vm.ShowAlarmNotifyOptions ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Event handlers ──────────────────────────────────────────────────

    private void OnAllDayToggled(object sender, RoutedEventArgs e)
    {
        if (_vm != null) _vm.AllDay = AllDaySwitch.IsOn;
    }

    private void OnStartDateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (_suppressStartDateChanged || _vm == null) return;
        if (sender.Date is { } d)
        {
            _suppressStartDateChanged = true;
            _vm.StartDate = new DateTime(d.Year, d.Month, d.Day);
            _suppressStartDateChanged = false;
        }
    }

    private static TimeSpan? TryParseTimeInput(string text)
    {
        if (TimeSpan.TryParseExact(text, @"hh\:mm", null, out var ts))
            return ts;
        // Allow 4-digit input without colon (e.g. "0930" → 09:30)
        if (text.Length == 4 && int.TryParse(text, out var num))
        {
            var h = num / 100;
            var m = num % 100;
            if (h is >= 0 and < 24 && m is >= 0 and < 60)
                return new TimeSpan(h, m, 0);
        }
        return null;
    }

    private void AutoFormatTimeBox(TextBox box, ref bool suppress)
    {
        var parsed = TryParseTimeInput(box.Text);
        if (parsed is { } ts && box.Text.Length == 4 && !box.Text.Contains(':'))
        {
            suppress = true;
            box.Text = ts.ToString(@"hh\:mm");
            box.SelectionStart = box.Text.Length;
            suppress = false;
        }
    }

    private void OnStartTimeChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressStartTimeChanged || _vm == null) return;
        var ts = TryParseTimeInput(StartTimeBox.Text);
        if (ts != null)
            _vm.StartTime = ts.Value;
    }

    private void OnStartTimeLostFocus(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        AutoFormatTimeBox(StartTimeBox, ref _suppressStartTimeChanged);
    }

    private void OnEndTimeChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressEndTimeChanged || _vm == null) return;
        var ts = TryParseTimeInput(EndTimeBox.Text);
        if (ts != null)
            _vm.EndTime = ts.Value;
    }

    private void OnEndTimeLostFocus(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        AutoFormatTimeBox(EndTimeBox, ref _suppressEndTimeChanged);
    }

    private void OnStartTimePickerChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressStartTimePickerChanged || _vm == null) return;
        if (StartTimePicker.SelectedItem is string s && TimeSpan.TryParseExact(s, @"hh\:mm", null, out var ts))
        {
            _vm.StartTime = ts;
            _suppressStartTimeChanged = true;
            StartTimeBox.Text = s;
            _suppressStartTimeChanged = false;
        }
    }

    private void OnEndTimePickerChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEndTimePickerChanged || _vm == null) return;
        if (EndTimePicker.SelectedItem is string s && TimeSpan.TryParseExact(s, @"hh\:mm", null, out var ts))
        {
            _vm.EndTime = ts;
            _suppressEndTimeChanged = true;
            EndTimeBox.Text = s;
            _suppressEndTimeChanged = false;
        }
    }

    private static void SyncTimePickerToValue(ComboBox picker, TimeSpan value, ref bool suppress)
    {
        // Snap to nearest 15-minute slot
        var totalMinutes = (int)value.TotalMinutes;
        var snapped = (int)Math.Round(totalMinutes / 15.0) * 15;
        if (snapped >= 1440) snapped = 1425;
        var index = snapped / 15;
        suppress = true;
        picker.SelectedIndex = index;
        suppress = false;
    }

    private void OnRepeatTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressRepeatTypeChanged || _vm == null) return;
        _vm.RepeatTypeIndex = RepeatTypePicker.SelectedIndex;
    }

    private void OnWeekdayChecked(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        _vm.WeekSun = WChkSun.IsChecked == true;
        _vm.WeekMon = WChkMon.IsChecked == true;
        _vm.WeekTue = WChkTue.IsChecked == true;
        _vm.WeekWed = WChkWed.IsChecked == true;
        _vm.WeekThu = WChkThu.IsChecked == true;
        _vm.WeekFri = WChkFri.IsChecked == true;
        _vm.WeekSat = WChkSat.IsChecked == true;
    }

    private void OnMonthlyRuleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressMonthlyRuleChanged || _vm == null) return;
        _vm.MonthlyRuleIndex = MonthlyRulePicker.SelectedIndex;
    }

    private void OnDomChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressDomChanged || _vm == null) return;
        if (int.TryParse(DomBox.Text, out var val))
            _vm.DayOfMonth = val;
    }

    private void OnWeekIndexChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressWeekIndexChanged || _vm == null) return;
        _vm.WeekIndexPickerIndex = WeekIndexPicker.SelectedIndex;
    }

    private void OnMonthlyWeekdayChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressMonthlyWeekdayChanged || _vm == null) return;
        _vm.MonthlyWeekdayIndex = MonthlyWeekdayPicker.SelectedIndex;
    }

    private void OnYearlyRuleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressYearlyRuleChanged || _vm == null) return;
        _vm.YearlyRuleIndex = YearlyRulePicker.SelectedIndex;
    }

    private void OnYearlyMonthChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressYearlyMonthChanged || _vm == null) return;
        if (int.TryParse(YearlyMonthBox.Text, out var val))
            _vm.YearlyMonth = val;
    }

    private void OnYearlyDayChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressYearlyDayChanged || _vm == null) return;
        if (int.TryParse(YearlyDayBox.Text, out var val))
            _vm.YearlyDay = val;
    }

    private void OnYearlyMonth2Changed(object sender, TextChangedEventArgs e)
    {
        if (_suppressYearlyMonth2Changed || _vm == null) return;
        if (int.TryParse(YearlyMonthBox2.Text, out var val))
            _vm.YearlyMonth = val;
    }

    private void OnYearlyWeekIndexChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressYearlyWeekIndexChanged || _vm == null) return;
        _vm.YearlyWeekIndexPickerIndex = YearlyWeekIndexPicker.SelectedIndex;
    }

    private void OnYearlyWeekdayChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressYearlyWeekdayChanged || _vm == null) return;
        _vm.YearlyWeekdayIndex = YearlyWeekdayPicker.SelectedIndex;
    }

    private void OnIntervalChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressIntervalChanged || _vm == null) return;
        if (int.TryParse(IntervalBox.Text, out var val))
            _vm.Interval = val;
    }

    private void OnEndDateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (_suppressEndDateChanged || _vm == null) return;
        if (sender.Date is { } d)
        {
            _suppressEndDateChanged = true;
            _vm.EndDate = new DateTime(d.Year, d.Month, d.Day);
            _suppressEndDateChanged = false;
        }
    }

    private void OnAdjustmentChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressAdjustmentChanged || _vm == null) return;
        _vm.AdjustmentIndex = AdjustmentPicker.SelectedIndex;
    }

    private void OnCalendarPickerChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressCalendarPickerChanged || _vm == null) return;
        _vm.SelectedCalendarIndex = CalendarPicker.SelectedIndex;
    }

    private void OnAlarmToggled(object sender, RoutedEventArgs e)
    {
        if (_vm != null) _vm.AlarmEnabled = AlarmSwitch.IsOn;
    }

    private void OnAlarmChecked(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        _vm.AlarmNotify15Min = ChkNotify15.IsChecked == true;
        _vm.AlarmNotify5Min  = ChkNotify5.IsChecked  == true;
        _vm.AlarmNotify1Min  = ChkNotify1.IsChecked  == true;
    }

    private async void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;

        if (_vm.RequiresRecurringEditScopeSelection)
        {
            var scope = await ShowRecurringScopeDialogAsync();
            if (scope == null) return; // user cancelled
            _vm.Save(scope);
        }
        else
        {
            _vm.Save();
        }
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
        => NavigationService.Instance.GoBack();

    private async System.Threading.Tasks.Task<RecurringEditScope?> ShowRecurringScopeDialogAsync()
    {
        // Build radio-button panel
        var thisOccurrenceRadio   = new RadioButton { Content = AppResources.EditThisOccurrence,   GroupName = "EditScope", IsChecked = true };
        var thisAndFollowingRadio = new RadioButton { Content = AppResources.EditThisAndFollowing,  GroupName = "EditScope" };
        var entireSeriesRadio     = new RadioButton { Content = AppResources.EditEntireSeries,       GroupName = "EditScope" };

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(thisOccurrenceRadio);
        panel.Children.Add(thisAndFollowingRadio);
        panel.Children.Add(entireSeriesRadio);

        var dialog = new ContentDialog
        {
            Title             = AppResources.EditRecurringEventTitle,
            Content           = panel,
            PrimaryButtonText = AppResources.SaveButton,
            CloseButtonText   = AppResources.CancelButton,
            XamlRoot          = XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
            return null;

        if (entireSeriesRadio.IsChecked == true)   return RecurringEditScope.EntireSeries;
        if (thisAndFollowingRadio.IsChecked == true) return RecurringEditScope.ThisAndFollowing;
        return RecurringEditScope.ThisOccurrence;
    }
}
