using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using NolumiaScheduler.WinUI.Helpers;
using NolumiaScheduler.Presentation.ViewModels;
using NolumiaScheduler.Presentation.Resources.Strings;

namespace NolumiaScheduler.WinUI.Presentation.Pages;

public sealed partial class EventEditPage : Page
{
    /// <summary>Set by a hosting window to override the default GoBack navigation on dismiss.</summary>
    public Action? DismissAction { get; set; }

    private void Dismiss()
    {
        if (DismissAction != null) DismissAction();
        else NavigationService.Instance.GoBack();
    }

    private EventEditViewModel? _vm;

    // Guards to prevent feedback loops when programmatically updating controls
    private bool _suppressStartTimeChanged;
    private bool _suppressEndTimeChanged;
    private bool _suppressStartTimePickerChanged;
    private bool _suppressEndTimePickerChanged;
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
    private bool _suppressAdjustmentDirectionChanged;
    private bool _suppressAdjustmentDaysChanged;
    private bool _suppressMonthlyLastDayChanged;
    private bool _suppressUseCustomIntervalChanged;
    private bool _suppressUseBusinessDayAdjustmentChanged;
    private bool _suppressCalendarPickerChanged;
    private bool _suppressWeekdayChanged;
    private bool _suppressAlarmChanged;
    private bool _suppressEventColorChanged;


    // 15-minute interval time items ("00:00" to "23:45"). Each picker has its own observable
    // copy so an off-grid (1-minute) value can be injected as a temporary sorted item to keep
    // SelectedIndex >= 0 — an editable ComboBox blanks its text whenever no item is selected.
    private static readonly string[] BaseTimeItems = [.. Enumerable.Range(0, 96).Select(i => $"{i / 4:D2}:{(i % 4) * 15:D2}")];
    private readonly System.Collections.ObjectModel.ObservableCollection<string> _startTimeItems = [.. BaseTimeItems];
    private readonly System.Collections.ObjectModel.ObservableCollection<string> _endTimeItems   = [.. BaseTimeItems];

    public EventEditPage()
    {
        InitializeComponent();

        // Static string labels
        TitleBox.PlaceholderText    = AppResources.EventTitlePlaceholder;
        LocationBox.PlaceholderText = AppResources.EventLocationPlaceholder;
        MemoBox.PlaceholderText     = AppResources.EventMemoPlaceholder;
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
        MonthlyLastDayChk.Content = AppResources.MonthlyLastDay;
        IntervalLabel.Text      = AppResources.IntervalLabel;
        UseCustomIntervalLabelText.Text = AppResources.UseCustomIntervalLabel;
        EndDateLabel.Text       = AppResources.EndDateLabel;
        UseBusinessDayAdjustmentLabelText.Text = AppResources.UseBusinessDayAdjustmentLabel;
        AdjustmentLabel.Text    = AppResources.AdjustmentLabel;
        AdjustmentBusinessDaysLabel.Text = AppResources.AdjustmentBusinessDaysLabel;
        AdjustmentDateTypeLabel.Text = AppResources.AdjustmentDateTypeLabel;
        AdjustmentScheduledDirectionLabel.Text = AppResources.AdjustmentLabel;
        BusinessCalendarLabel.Text = AppResources.BusinessCalendarLabel;
        ColorLabel.Text         = AppResources.ColorLabel;
        AlarmLabel.Text         = AppResources.AlarmLabel;
        AlarmEnableLabel.Text   = AppResources.AlarmEnable;
        Notify15Label.Text      = AppResources.AlarmNotify15Min;
        Notify5Label.Text       = AppResources.AlarmNotify5Min;
        Notify1Label.Text       = AppResources.AlarmNotify1Min;
        Notify0Label.Text       = AppResources.AlarmNotifyAtStart;
        SaveBtn.Content         = AppResources.SaveButton;
        FloatingSaveBtn.Content = AppResources.SaveButton;
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
                // Override times when opened from drag/resize (params carry the new time)
                if (p.OccurrenceStartMinute.HasValue)
                    _vm.StartTime = TimeSpan.FromMinutes(p.OccurrenceStartMinute.Value);
                if (p.OccurrenceEndMinute.HasValue)
                    _vm.EndTime = TimeSpan.FromMinutes(p.OccurrenceEndMinute.Value);
            }
            else if (p.CloneEventId != null)
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
                _vm.LoadEventForClone(p.CloneEventId, occKey);
            }
            else if (p.StartDate != null
                     && DateTime.TryParse(p.StartDate, out var startDt))
            {
                var dateOnly = DateOnly.FromDateTime(startDt);
                _vm.InitializeNewEvent(dateOnly, p.StartMinute ?? (9 * 60), p.EndMinute);
                if (p.AllDay) _vm.AllDay = true;
            }
        }

        _vm.SaveCompleted += Dismiss;
        _vm.DeleteCompleted += Dismiss;
        _vm.PropertyChanged += OnVmPropertyChanged;

        // Show delete button only when editing an existing event
        if (_vm.IsEditing)
        {
            DeleteBtn.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            DeleteBtn.Content = AppResources.DeleteButton;
        }

        BindViewModel();
    }

    private void BindViewModel()
    {
        if (_vm == null) return;

        // Title / Location / Memo
        TitleBox.Text    = _vm.Title;
        LocationBox.Text = _vm.Location;
        MemoBox.Text     = _vm.Memo;
        TitleBox.TextChanged    += (_, _) => _vm.Title    = TitleBox.Text;
        LocationBox.TextChanged += (_, _) => _vm.Location = LocationBox.Text;
        MemoBox.TextChanged     += (_, _) => _vm.Memo     = MemoBox.Text;

        // All Day
        AllDaySwitch.IsOn = _vm.AllDay;

        // Start date
        _suppressStartDateChanged = true;
        StartDatePicker.Date = new DateTimeOffset(_vm.StartDate);
        _suppressStartDateChanged = false;

        // Time pickers (editable ComboBox)
        StartTimePicker.ItemsSource = _startTimeItems;
        EndTimePicker.ItemsSource = _endTimeItems;

        ApplyTimeToPicker(StartTimePicker, _startTimeItems, _vm.StartTime,
            ref _suppressStartTimeChanged, ref _suppressStartTimePickerChanged);
        ApplyTimeToPicker(EndTimePicker, _endTimeItems, _vm.EndTime,
            ref _suppressEndTimeChanged, ref _suppressEndTimePickerChanged);

        // Recurrence pickers
        RepeatTypePicker.ItemsSource   = EventEditViewModel.RepeatTypeItems;
        MonthlyRulePicker.ItemsSource  = EventEditViewModel.MonthlyRuleItems;
        YearlyRulePicker.ItemsSource   = EventEditViewModel.YearlyRuleItems;
        WeekIndexPicker.ItemsSource    = EventEditViewModel.WeekIndexItems;
        MonthlyWeekdayPicker.ItemsSource = EventEditViewModel.WeekdayItems;
        YearlyWeekIndexPicker.ItemsSource = EventEditViewModel.WeekIndexItems;
        YearlyWeekdayPicker.ItemsSource   = EventEditViewModel.WeekdayItems;
        AdjustmentDirectionPicker.ItemsSource = EventEditViewModel.AdjustmentDirectionItems;

        _suppressRepeatTypeChanged = true;
        RepeatTypePicker.SelectedIndex = _vm.RepeatTypeIndex;
        _suppressRepeatTypeChanged = false;

        _suppressMonthlyRuleChanged = true;
        MonthlyRulePicker.SelectedIndex = _vm.MonthlyRuleIndex;
        _suppressMonthlyRuleChanged = false;

        _suppressYearlyRuleChanged = true;
        YearlyRulePicker.SelectedIndex = _vm.YearlyRuleIndex;
        _suppressYearlyRuleChanged = false;

        // Weekday checkboxes for weekly recurrence.
        // Suppress the change handler while populating: it reads every checkbox back into the
        // VM, so an unsuppressed mid-population assignment would clobber weekday flags that have
        // not been applied to their checkbox yet (leaving only the first selected day checked).
        _suppressWeekdayChanged = true;
        WChkSun.IsChecked = _vm.WeekSun;
        WChkMon.IsChecked = _vm.WeekMon;
        WChkTue.IsChecked = _vm.WeekTue;
        WChkWed.IsChecked = _vm.WeekWed;
        WChkThu.IsChecked = _vm.WeekThu;
        WChkFri.IsChecked = _vm.WeekFri;
        WChkSat.IsChecked = _vm.WeekSat;
        _suppressWeekdayChanged = false;

        // Monthly
        _suppressDomChanged = true;
        DomBox.Text = _vm.DayOfMonth.ToString();
        _suppressDomChanged = false;

        _suppressMonthlyLastDayChanged = true;
        MonthlyLastDayChk.IsChecked = _vm.MonthlyLastDay;
        _suppressMonthlyLastDayChanged = false;

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
        _suppressUseCustomIntervalChanged = true;
        UseCustomIntervalSwitch.IsOn = _vm.UseCustomInterval;
        _suppressUseCustomIntervalChanged = false;

        _suppressIntervalChanged = true;
        IntervalBox.Text = _vm.Interval.ToString();
        _suppressIntervalChanged = false;

        IntervalUnitLabel.Text = _vm.IntervalUnitLabel;

        _suppressEndDateChanged = true;
        EndDatePicker.Date = _vm.HasEndDate ? new DateTimeOffset(_vm.EndDate) : (DateTimeOffset?)null;
        _suppressEndDateChanged = false;

        // Adjustment
        _suppressUseBusinessDayAdjustmentChanged = true;
        UseBusinessDayAdjustmentSwitch.IsOn = _vm.UseBusinessDayAdjustment;
        _suppressUseBusinessDayAdjustmentChanged = false;

        AdjustmentDateTypePicker.ItemsSource = EventEditViewModel.AdjustmentDateTypeItems;
        AdjustmentScheduledDirectionPicker.ItemsSource = EventEditViewModel.AdjustmentDirectionItems;
        AdjustmentDateTypePicker.SelectedIndex = _vm.AdjustmentDateTypeIndex;
        AdjustmentScheduledDirectionPicker.SelectedIndex = _vm.AdjustmentDirectionIndex;
        AdjustmentDateTypePicker.SelectionChanged += OnAdjustmentDateTypeChanged;
        AdjustmentScheduledDirectionPicker.SelectionChanged += OnAdjustmentScheduledDirectionChanged;

        _suppressAdjustmentDirectionChanged = true;
        AdjustmentDirectionPicker.SelectedIndex = _vm.AdjustmentDirectionIndex;
        _suppressAdjustmentDirectionChanged = false;

        _suppressAdjustmentDaysChanged = true;
        AdjustmentDaysBox.Text = _vm.AdjustmentBusinessDays.ToString();
        _suppressAdjustmentDaysChanged = false;

        CalendarPicker.ItemsSource = _vm.AvailableCalendarNames;
        _suppressCalendarPickerChanged = true;
        CalendarPicker.SelectedIndex = _vm.SelectedCalendarIndex;
        _suppressCalendarPickerChanged = false;

        // Color picker: one item per palette entry, rendered as swatch + localized name.
        _suppressEventColorChanged = true;
        EventColorPicker.Items.Clear();
        var colorNames = EventEditViewModel.ColorItems;
        for (var i = 0; i < EventEditViewModel.ColorKeys.Length; i++)
        {
            var swatch = new Microsoft.UI.Xaml.Shapes.Ellipse
            {
                Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center,
                Fill = new SolidColorBrush(
                    NolumiaScheduler.Presentation.Helpers.WinColors.ForEventColor(EventEditViewModel.ColorKeys[i]))
            };
            var item = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            item.Children.Add(swatch);
            item.Children.Add(new TextBlock { Text = colorNames[i], VerticalAlignment = VerticalAlignment.Center });
            EventColorPicker.Items.Add(item);
        }
        EventColorPicker.SelectedIndex = _vm.SelectedColorIndex;
        _suppressEventColorChanged = false;
        UpdateColorHeaderSwatch();

        // Rarely used sections start collapsed; open them only when already in use
        // so an existing recurrence/color is immediately visible while editing.
        RepeatExpander.IsExpanded = _vm.IsRecurring;
        ColorExpander.IsExpanded  = _vm.HasCustomColor;

        // Alarm
        // Suppress the change handlers while populating: OnAlarmChecked reads every checkbox
        // back into the VM, so an unsuppressed mid-population event would clobber alarm flags
        // that have not been applied to their checkbox yet (leaving only the first one checked).
        _suppressAlarmChanged = true;
        AlarmSwitch.IsOn       = _vm.AlarmEnabled;
        ChkNotify15.IsChecked  = _vm.AlarmNotify15Min;
        ChkNotify5.IsChecked   = _vm.AlarmNotify5Min;
        ChkNotify1.IsChecked   = _vm.AlarmNotify1Min;
        ChkNotify0.IsChecked   = _vm.AlarmNotifyAtStart;
        _suppressAlarmChanged = false;

        // Validation
        ValidationBorder.Visibility = _vm.HasValidationError ? Visibility.Visible : Visibility.Collapsed;
        ValidationText.Text = _vm.ValidationError;

        // Center the selected item in the time picker dropdowns when they open
        StartTimePicker.DropDownOpened += OnTimePickerDropDownOpened;
        EndTimePicker.DropDownOpened   += OnTimePickerDropDownOpened;

        // Apply initial section visibility
        ApplySectionVisibility();
    }

    // Push the VM's recurrence selector values into the weekday checkboxes and the monthly/yearly
    // inputs. Used when the recurrence type changes so start-date-based defaults appear in the UI.
    private void SyncRecurrenceInputsFromVm()
    {
        if (_vm == null) return;

        _suppressWeekdayChanged = true;
        WChkSun.IsChecked = _vm.WeekSun;
        WChkMon.IsChecked = _vm.WeekMon;
        WChkTue.IsChecked = _vm.WeekTue;
        WChkWed.IsChecked = _vm.WeekWed;
        WChkThu.IsChecked = _vm.WeekThu;
        WChkFri.IsChecked = _vm.WeekFri;
        WChkSat.IsChecked = _vm.WeekSat;
        _suppressWeekdayChanged = false;

        _suppressDomChanged = true;
        DomBox.Text = _vm.DayOfMonth.ToString();
        _suppressDomChanged = false;

        _suppressWeekIndexChanged = true;
        WeekIndexPicker.SelectedIndex = _vm.WeekIndexPickerIndex;
        _suppressWeekIndexChanged = false;

        _suppressMonthlyWeekdayChanged = true;
        MonthlyWeekdayPicker.SelectedIndex = _vm.MonthlyWeekdayIndex;
        _suppressMonthlyWeekdayChanged = false;

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
    }

    private void OnTimePickerDropDownOpened(object? sender, object e)
    {
        if (sender is ComboBox picker && picker.SelectedIndex >= 0 && picker.Items.Count > 0)
            _ = CenterComboBoxSelectionAsync(picker, picker.SelectedIndex);
    }

    // The dropdown's ScrollViewer lives inside a Popup, which is NOT reachable by walking the
    // visual tree down from the ComboBox. It also isn't measured the instant the dropdown opens,
    // so retry a few times until the popup exposes a ScrollViewer with an extent, then center
    // the target item in the viewport (clamped to the top for items near the start).
    private static async System.Threading.Tasks.Task CenterComboBoxSelectionAsync(ComboBox comboBox, int index)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            var sv = FindOpenDropDownScrollViewer(comboBox);
            if (sv is { ExtentHeight: > 0 } && comboBox.Items.Count > 0)
            {
                var itemHeight = sv.ExtentHeight / comboBox.Items.Count;
                var target = index * itemHeight - (sv.ViewportHeight - itemHeight) / 2;
                sv.ChangeView(null, Math.Max(0, target), null, true);
                return;
            }
            await System.Threading.Tasks.Task.Delay(16);
        }
    }

    private static ScrollViewer? FindOpenDropDownScrollViewer(ComboBox comboBox)
    {
        // Use the ComboBox's OWN dropdown popup (part of its control template) so we always
        // scroll the right list. Searching all open popups for the XamlRoot can return another
        // ComboBox's popup (e.g. Start vs End), which left the End picker uncentered.
        var popup = FindDescendant<Microsoft.UI.Xaml.Controls.Primitives.Popup>(comboBox);
        if (popup?.Child is DependencyObject child)
            return FindDescendant<ScrollViewer>(child);
        return null;
    }

    private static T? FindDescendant<T>(DependencyObject? parent) where T : DependencyObject
    {
        if (parent == null) return null;
        if (parent is T self) return self;
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (FindDescendant<T>(child) is { } result) return result;
        }
        return null;
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
                if (_vm.HasValidationError)
                    MainScrollViewer.ChangeView(null, 0, null);
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
            case nameof(EventEditViewModel.IsDayOfMonthInputEnabled):
            case nameof(EventEditViewModel.UseCustomInterval):
            case nameof(EventEditViewModel.UseBusinessDayAdjustment):
            case nameof(EventEditViewModel.IsScheduledDateMode):
            case nameof(EventEditViewModel.IsBaseDateMode):
            case nameof(EventEditViewModel.ShowAlarmNotifyOptions):
                ApplySectionVisibility();
                break;

            case nameof(EventEditViewModel.IntervalUnitLabel):
                IntervalUnitLabel.Text = _vm.IntervalUnitLabel;
                break;

            case nameof(EventEditViewModel.Interval):
                if (!_suppressIntervalChanged)
                {
                    _suppressIntervalChanged = true;
                    IntervalBox.Text = _vm.Interval.ToString();
                    _suppressIntervalChanged = false;
                }
                break;

            case nameof(EventEditViewModel.AdjustmentBusinessDays):
                // Toggling UseBusinessDayAdjustment restores the saved value; sync the textbox.
                if (!_suppressAdjustmentDaysChanged)
                {
                    _suppressAdjustmentDaysChanged = true;
                    AdjustmentDaysBox.Text = _vm.AdjustmentBusinessDays.ToString();
                    _suppressAdjustmentDaysChanged = false;
                }
                break;

            case nameof(EventEditViewModel.RepeatTypeIndex):
                if (!_suppressRepeatTypeChanged)
                {
                    _suppressRepeatTypeChanged = true;
                    RepeatTypePicker.SelectedIndex = _vm.RepeatTypeIndex;
                    _suppressRepeatTypeChanged = false;
                }
                // Picking a recurrence type seeds the selectors from the start date in the VM;
                // push those values into the weekday/day/month controls so the UI reflects them.
                SyncRecurrenceInputsFromVm();
                break;

            case nameof(EventEditViewModel.HasEndDate):
            case nameof(EventEditViewModel.EndDate):
                if (!_suppressEndDateChanged)
                {
                    _suppressEndDateChanged = true;
                    EndDatePicker.Date = _vm.HasEndDate ? new DateTimeOffset(_vm.EndDate) : (DateTimeOffset?)null;
                    _suppressEndDateChanged = false;
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
                    ApplyTimeToPicker(StartTimePicker, _startTimeItems, _vm.StartTime,
                        ref _suppressStartTimeChanged, ref _suppressStartTimePickerChanged);
                break;

            case nameof(EventEditViewModel.EndTime):
                if (!_suppressEndTimeChanged)
                    ApplyTimeToPicker(EndTimePicker, _endTimeItems, _vm.EndTime,
                        ref _suppressEndTimeChanged, ref _suppressEndTimePickerChanged);
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

        // The day input is meaningless once "末日" (month-end) is checked.
        DomBox.IsEnabled = _vm.IsDayOfMonthInputEnabled;

        // The interval input only appears when the user opts into a custom interval.
        IntervalInputSection.Visibility = _vm.UseCustomInterval
            ? Visibility.Visible : Visibility.Collapsed;

        // The adjustment detail section only appears when the toggle is on.
        AdjustmentDetailSection.Visibility = _vm.UseBusinessDayAdjustment
            ? Visibility.Visible : Visibility.Collapsed;

        // In Scheduled Date mode: show the simple direction picker, hide the N-days input.
        // In Base Date mode: show N-days + direction, hide the scheduled direction picker.
        AdjustmentDaysSection.Visibility = _vm.IsBaseDateMode
            ? Visibility.Visible : Visibility.Collapsed;
        AdjustmentScheduledDirectionSection.Visibility = _vm.IsScheduledDateMode
            ? Visibility.Visible : Visibility.Collapsed;

        // Show calendar picker whenever adjustment is ON and calendars are available.
        CalendarPickerSection.Visibility = (_vm.UseBusinessDayAdjustment && _vm.HasAvailableCalendars)
            ? Visibility.Visible : Visibility.Collapsed;

        AlarmNotifySection.Visibility  = _vm.ShowAlarmNotifyOptions ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Event handlers ──────────────────────────────────────────────────

    private void OnAllDayToggled(object sender, RoutedEventArgs e)
    {
        _vm?.AllDay = AllDaySwitch.IsOn;
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


    private void OnStartTimePickerChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressStartTimePickerChanged || _vm == null) return;
        if (StartTimePicker.SelectedItem is string s && TimeSpan.TryParseExact(s, @"hh\:mm", null, out var ts))
        {
            _vm.SetStartTimePreservingDuration(ts);
            _suppressStartTimeChanged = true;
            StartTimePicker.Text = s;
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
            EndTimePicker.Text = s;
            _suppressEndTimeChanged = false;
        }
    }

    private void OnStartTimeTextSubmitted(ComboBox sender, ComboBoxTextSubmittedEventArgs args)
        => HandleTimeTextSubmitted(sender, args, _startTimeItems,
            t => _vm?.SetStartTimePreservingDuration(t), () => _vm?.StartTime,
            ref _suppressStartTimeChanged, ref _suppressStartTimePickerChanged);

    private void OnEndTimeTextSubmitted(ComboBox sender, ComboBoxTextSubmittedEventArgs args)
        => HandleTimeTextSubmitted(sender, args, _endTimeItems,
            t => _vm?.EndTime = t, () => _vm?.EndTime,
            ref _suppressEndTimeChanged, ref _suppressEndTimePickerChanged);

    private void HandleTimeTextSubmitted(ComboBox picker, ComboBoxTextSubmittedEventArgs args,
        System.Collections.ObjectModel.ObservableCollection<string> items,
        Action<TimeSpan> setTime, Func<TimeSpan?> getTime,
        ref bool suppressTime, ref bool suppressPicker)
    {
        if (_vm == null || suppressTime) return;
        // Always mark the submission Handled so the ComboBox doesn't try to re-select / blank
        // the editable text after we apply the value.
        args.Handled = true;
        if (FormatTimeInput(args.Text) is { } formatted &&
            TimeSpan.TryParseExact(formatted, @"hh\:mm", null, out var ts))
        {
            setTime(ts);
            ApplyTimeToPicker(picker, items, ts, ref suppressTime, ref suppressPicker);
        }
        else if (getTime() is { } cur)
        {
            ApplyTimeToPicker(picker, items, cur, ref suppressTime, ref suppressPicker);
        }
    }

    /// <summary>
    /// Parses free-form time input (e.g. "0930", "9:30", "09:30") and returns formatted "HH:mm" or null if invalid.
    /// </summary>
    private static string? FormatTimeInput(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim();

        // Already in HH:mm format
        if (TimeSpan.TryParseExact(text, @"hh\:mm", null, out var ts1))
            return ts1.ToString(@"hh\:mm");

        // 4-digit without colon (e.g. "0930")
        if (text.Length == 4 && int.TryParse(text, out var num4))
        {
            var h = num4 / 100;
            var m = num4 % 100;
            if (h is >= 0 and < 24 && m is >= 0 and < 60)
                return new TimeSpan(h, m, 0).ToString(@"hh\:mm");
        }

        // 3-digit (e.g. "930" → 9:30)
        if (text.Length == 3 && int.TryParse(text, out var num3))
        {
            var h = num3 / 100;
            var m = num3 % 100;
            if (h is >= 0 and < 24 && m is >= 0 and < 60)
                return new TimeSpan(h, m, 0).ToString(@"hh\:mm");
        }

        // 1-2 digit (hour only, e.g. "9" → 09:00)
        if (text.Length <= 2 && int.TryParse(text, out var hour) && hour is >= 0 and < 24)
            return new TimeSpan(hour, 0, 0).ToString(@"hh\:mm");

        return null;
    }

    // Make the picker display `value`. Keeping SelectedIndex valid at all times is essential —
    // an editable ComboBox blanks its editable text whenever SelectedItem is null, which made
    // off-grid (1-minute) values disappear on Enter, focus loss, or re-open. For off-grid
    // values, splice the time string into the sorted item list as a temporary entry and select
    // it; remove any previously-inserted off-grid entry so only one extra item lingers.
    private static void ApplyTimeToPicker(ComboBox picker,
        System.Collections.ObjectModel.ObservableCollection<string> items, TimeSpan value,
        ref bool suppressTime, ref bool suppressPicker)
    {
        suppressTime = true;
        suppressPicker = true;
        try
        {
            var clamped = Math.Clamp((int)value.TotalMinutes, 0, 1439);
            var label = $"{clamped / 60:D2}:{clamped % 60:D2}";

            // Drop any prior off-grid placeholder so the list shape stays predictable.
            for (var i = items.Count - 1; i >= 0; i--)
            {
                if (!IsOnGridSlot(items[i]))
                    items.RemoveAt(i);
            }

            int index;
            if (clamped % 15 == 0)
            {
                index = clamped / 15;
            }
            else
            {
                // Insert sorted so the user sees the off-grid time in the right position when
                // they scroll the dropdown.
                index = 0;
                while (index < items.Count && string.CompareOrdinal(items[index], label) < 0) index++;
                items.Insert(index, label);
            }
            picker.SelectedIndex = index;
            picker.Text = label;
        }
        finally
        {
            suppressTime = false;
            suppressPicker = false;
        }
    }

    private static bool IsOnGridSlot(string label)
        => TimeSpan.TryParseExact(label, @"hh\:mm", null, out var ts) && (int)ts.TotalMinutes % 15 == 0;

    private void OnRepeatTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressRepeatTypeChanged || _vm == null) return;
        _vm.RepeatTypeIndex = RepeatTypePicker.SelectedIndex;
    }

    private void OnWeekdayChecked(object sender, RoutedEventArgs e)
    {
        if (_suppressWeekdayChanged || _vm == null) return;
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
        _suppressEndDateChanged = true;
        if (sender.Date is { } d)
        {
            _vm.HasEndDate = true;
            _vm.EndDate = new DateTime(d.Year, d.Month, d.Day);
        }
        else
        {
            _vm.HasEndDate = false;
        }
        _suppressEndDateChanged = false;
    }

    private void OnAdjustmentDateTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_vm == null) return;
        var picker = sender as ComboBox ?? AdjustmentDateTypePicker;
        _vm.AdjustmentDateTypeIndex = picker.SelectedIndex;
    }

    private void OnAdjustmentScheduledDirectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_vm == null) return;
        var picker = sender as ComboBox ?? AdjustmentScheduledDirectionPicker;
        _vm.AdjustmentDirectionIndex = picker.SelectedIndex;
        // Keep the Base Date direction picker in sync.
        if (!_suppressAdjustmentDirectionChanged)
        {
            _suppressAdjustmentDirectionChanged = true;
            AdjustmentDirectionPicker.SelectedIndex = picker.SelectedIndex;
            _suppressAdjustmentDirectionChanged = false;
        }
    }

    private void OnMonthlyLastDayChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressMonthlyLastDayChanged || _vm == null) return;
        _vm.MonthlyLastDay = MonthlyLastDayChk.IsChecked == true;
    }

    private void OnUseCustomIntervalToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressUseCustomIntervalChanged || _vm == null) return;
        _vm.UseCustomInterval = UseCustomIntervalSwitch.IsOn;
    }

    private void OnUseBusinessDayAdjustmentToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressUseBusinessDayAdjustmentChanged || _vm == null) return;
        _vm.UseBusinessDayAdjustment = UseBusinessDayAdjustmentSwitch.IsOn;
    }

    // Floating Save button: visible when not scrolled to the bottom (where the in-content
    // Save button is accessible). Hides once the bottom is within reach.
    private void OnMainScrollViewerViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (e.IsIntermediate) return;
        var atBottom = MainScrollViewer.ScrollableHeight - MainScrollViewer.VerticalOffset < 80;
        FloatingSaveBorder.Visibility = atBottom ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnAdjustmentDirectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressAdjustmentDirectionChanged || _vm == null) return;
        _vm.AdjustmentDirectionIndex = AdjustmentDirectionPicker.SelectedIndex;
        // Keep the Scheduled Date direction picker in sync.
        AdjustmentScheduledDirectionPicker.SelectedIndex = AdjustmentDirectionPicker.SelectedIndex;
    }

    private void OnAdjustmentDaysChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressAdjustmentDaysChanged || _vm == null) return;
        if (int.TryParse(AdjustmentDaysBox.Text, out var val))
            _vm.AdjustmentBusinessDays = val;
    }

    private void OnCalendarPickerChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressCalendarPickerChanged || _vm == null) return;
        _vm.SelectedCalendarIndex = CalendarPicker.SelectedIndex;
    }

    private void OnEventColorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEventColorChanged || _vm == null) return;
        if (EventColorPicker.SelectedIndex >= 0)
        {
            _vm.SelectedColorIndex = EventColorPicker.SelectedIndex;
            UpdateColorHeaderSwatch();
        }
    }

    // Show the selected color next to the header text so the choice stays
    // visible while the expander is collapsed.
    private void UpdateColorHeaderSwatch()
    {
        if (_vm == null) return;
        ColorHeaderSwatch.Fill = new SolidColorBrush(
            NolumiaScheduler.Presentation.Helpers.WinColors.ForEventColor(_vm.SelectedColorKey));
    }

    private void OnAlarmToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressAlarmChanged) return;
        _vm?.AlarmEnabled = AlarmSwitch.IsOn;
    }

    private void OnAlarmChecked(object sender, RoutedEventArgs e)
    {
        if (_suppressAlarmChanged || _vm == null) return;
        _vm.AlarmNotify15Min = ChkNotify15.IsChecked == true;
        _vm.AlarmNotify5Min  = ChkNotify5.IsChecked  == true;
        _vm.AlarmNotify1Min  = ChkNotify1.IsChecked  == true;
        _vm.AlarmNotifyAtStart = ChkNotify0.IsChecked == true;
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

    private async void OnDeleteClicked(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;

        if (_vm.IsRecurring)
        {
            var scope = await ShowRecurringDeleteScopeDialogAsync();
            if (scope == null) return;
            switch (scope)
            {
                case RecurringEditScope.ThisOccurrence:
                    _vm.DeleteOccurrence();
                    break;
                case RecurringEditScope.ThisAndFollowing:
                    _vm.DeleteOccurrenceAndFollowing();
                    break;
                case RecurringEditScope.EntireSeries:
                    _vm.DeleteEntireEvent();
                    break;
            }
        }
        else
        {
            var dialog = new ContentDialog
            {
                Title             = AppResources.DeleteEventTitle,
                Content           = _vm.Title,
                PrimaryButtonText = AppResources.DeleteButton,
                CloseButtonText   = AppResources.CancelButton,
                XamlRoot          = XamlRoot
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
                _vm.DeleteEntireEvent();
        }
    }

    private async System.Threading.Tasks.Task<RecurringEditScope?> ShowRecurringDeleteScopeDialogAsync()
    {
        var thisOccurrenceRadio   = new RadioButton { Content = AppResources.DeleteOccurrence,        GroupName = "DeleteScope", IsChecked = true };
        var thisAndFollowingRadio = new RadioButton { Content = AppResources.DeleteThisAndFollowing,   GroupName = "DeleteScope" };
        var entireSeriesRadio     = new RadioButton { Content = AppResources.DeleteAllOccurrences,     GroupName = "DeleteScope" };

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(thisOccurrenceRadio);
        panel.Children.Add(thisAndFollowingRadio);
        panel.Children.Add(entireSeriesRadio);

        var dialog = new ContentDialog
        {
            Title             = AppResources.DeleteEventTitle,
            Content           = panel,
            PrimaryButtonText = AppResources.DeleteButton,
            CloseButtonText   = AppResources.CancelButton,
            XamlRoot          = XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return null;

        if (entireSeriesRadio.IsChecked == true)     return RecurringEditScope.EntireSeries;
        if (thisAndFollowingRadio.IsChecked == true) return RecurringEditScope.ThisAndFollowing;
        return RecurringEditScope.ThisOccurrence;
    }

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
