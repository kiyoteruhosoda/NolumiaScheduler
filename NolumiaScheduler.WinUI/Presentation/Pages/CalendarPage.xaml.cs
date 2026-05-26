using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using NolumiaScheduler.Presentation.Controls;
using NolumiaScheduler.Presentation.Services;
using NolumiaScheduler.WinUI.Presentation.Controls;
using NolumiaScheduler.WinUI.Presentation.Services;
using NolumiaScheduler.Presentation.ViewModels;
using NolumiaScheduler.WinUI.Helpers;
using NolumiaScheduler.WinUI.Presentation.Pages;
using NolumiaScheduler.Presentation.Resources.Strings;

namespace NolumiaScheduler.WinUI.Presentation.Pages;

public sealed partial class CalendarPage : Page
{
    private CalendarViewModel? _vm;
    private readonly IWeekInteractionCompletionService _interactionCompletionService;
    private readonly List<EventEditWindow> _openEditWindows = [];

    public CalendarPage()
    {
        InitializeComponent();
        _interactionCompletionService = NolumiaScheduler.WinUI.App.Services.GetRequiredService<IWeekInteractionCompletionService>();

        // Wire callback so resize/drag completions open EventEditWindow (not ContentFrame navigation)
        if (_interactionCompletionService is NavigateWeekInteractionCompletionService svc)
            svc.OpenEditWindowCallback = OpenEditWindow;

        // Static strings
        BtnToday.Content     = AppResources.TodayButton;
        LblSun.Text          = AppResources.DaySun;
        LblMon.Text          = AppResources.DayMon;
        LblTue.Text          = AppResources.DayTue;
        LblWed.Text          = AppResources.DayWed;
        LblThu.Text          = AppResources.DayThu;
        LblFri.Text          = AppResources.DayFri;
        LblSat.Text          = AppResources.DaySat;
        NoEventsLabel.Text   = AppResources.NoEventsLabel;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _vm = NolumiaScheduler.WinUI.App.Services.GetRequiredService<CalendarViewModel>();
        _vm.ReloadCurrentMonth();
        if (e.Parameter as string == "Month")
            _vm.SwitchToMonthViewCommand.Execute(null);
        else
            _vm.SwitchToWeekViewCommand.Execute(null);
        BindViewModel();
    }

    private void BindViewModel()
    {
        if (_vm == null) return;

        // Initial bind
        MonthYearLabel.Text        = _vm.MonthYearTitle;
        MonthGrid.ItemsSource      = _vm.DayCells;
        EventsList.ItemsSource     = _vm.SelectedDayEvents;
        WeekView.WeekTimeSlots     = _vm.WeekTimeSlots;
        WeekView.WeekDayColumns    = _vm.WeekDayColumns;
        WeekView.WeekAllDayEventBlocks = _vm.WeekAllDayEventBlocks;
        WeekView.WeekStartDate     = _vm.WeekStartDate;
        WeekView.WeekCanvasHeight  = CalendarViewModel.WeekCanvasHeight;
        WeekView.CurrentTimeLineTop = CalendarViewModel.CurrentTimeLineTop;
        WeekView.IsCurrentWeek     = _vm.IsCurrentWeek;
        UpdateViewMode();
        UpdateSelectedDayPanel();

        _vm.PropertyChanged += (_, args) =>
        {
            switch (args.PropertyName)
            {
                case nameof(CalendarViewModel.MonthYearTitle):
                    MonthYearLabel.Text = _vm.MonthYearTitle;
                    break;
                case nameof(CalendarViewModel.DayCells):
                    MonthGrid.ItemsSource = _vm.DayCells;
                    break;
                case nameof(CalendarViewModel.IsMonthMode):
                    UpdateViewMode();
                    break;
                case nameof(CalendarViewModel.IsMonthModeAndHasSelectedDay):
                    UpdateSelectedDayPanel();
                    break;
                case nameof(CalendarViewModel.HasSelectedDay):
                    UpdateSelectedDayPanel();
                    break;
                case nameof(CalendarViewModel.SelectedDayLabel):
                    SelectedDayLabel.Text = _vm.SelectedDayLabel;
                    break;
                case nameof(CalendarViewModel.SelectedDayEvents):
                    EventsList.ItemsSource = _vm.SelectedDayEvents;
                    NoEventsLabel.Visibility = _vm.SelectedDayHasNoEvents ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
                    break;
                case nameof(CalendarViewModel.SelectedDayHasNoEvents):
                    NoEventsLabel.Visibility = _vm.SelectedDayHasNoEvents ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
                    break;
                case nameof(CalendarViewModel.SelectedDayIsHoliday):
                case nameof(CalendarViewModel.SelectedDayHolidayText):
                    UpdateHolidayLabel();
                    break;
                case nameof(CalendarViewModel.WeekDayColumns):
                    WeekView.WeekDayColumns = _vm.WeekDayColumns;
                    break;
                case nameof(CalendarViewModel.WeekTimeSlots):
                    WeekView.WeekTimeSlots = _vm.WeekTimeSlots;
                    break;
                case nameof(CalendarViewModel.WeekAllDayEventBlocks):
                    WeekView.WeekAllDayEventBlocks = _vm.WeekAllDayEventBlocks;
                    break;
                case nameof(CalendarViewModel.WeekStartDate):
                    WeekView.WeekStartDate = _vm.WeekStartDate;
                    break;
                case nameof(CalendarViewModel.IsCurrentWeek):
                    WeekView.CurrentTimeLineTop = CalendarViewModel.CurrentTimeLineTop;
                    WeekView.IsCurrentWeek = _vm.IsCurrentWeek;
                    break;
                case nameof(CalendarViewModel.WeekCanvasHeight):
                    WeekView.WeekCanvasHeight = CalendarViewModel.WeekCanvasHeight;
                    break;
                case nameof(CalendarViewModel.CurrentTimeLineTop):
                    WeekView.CurrentTimeLineTop = CalendarViewModel.CurrentTimeLineTop;
                    break;
            }
        };
    }

    private void OnCalendarAreaSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_vm == null || !_vm.IsMonthMode || e.NewSize.Height <= 0) return;
        var rowCount = Math.Max(1, (_vm.DayCells.Count + 6) / 7);
        _vm.DayCellHeight = e.NewSize.Height / rowCount;
    }

    private void UpdateViewMode()
    {
        if (_vm == null) return;
        var isMonth = _vm.IsMonthMode;
        MonthGrid.Visibility           = isMonth ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        WeekView.Visibility            = isMonth ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
        DayOfWeekHeader.Visibility     = isMonth ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        GridSeparator.Visibility       = isMonth ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        SelectedDayPanel.Visibility    = (isMonth && _vm.HasSelectedDay) ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

        if (isMonth && CalendarAreaSentinel.ActualHeight > 0)
        {
            var rowCount = Math.Max(1, (_vm.DayCells.Count + 6) / 7);
            _vm.DayCellHeight = CalendarAreaSentinel.ActualHeight / rowCount;
        }
        else if (!isMonth)
        {
            WeekView.CurrentTimeLineTop = CalendarViewModel.CurrentTimeLineTop;
            WeekView.IsCurrentWeek = _vm.IsCurrentWeek;
            WeekView.RequestScroll();
        }
    }

    private void UpdateSelectedDayPanel()
    {
        if (_vm == null) return;
        SelectedDayPanel.Visibility = (_vm.IsMonthMode && _vm.HasSelectedDay) ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        if (_vm.HasSelectedDay)
        {
            SelectedDayLabel.Text      = _vm.SelectedDayLabel;
            EventsList.ItemsSource     = _vm.SelectedDayEvents;
            NoEventsLabel.Visibility   = _vm.SelectedDayHasNoEvents ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
            UpdateHolidayLabel();
        }
    }

    private void UpdateHolidayLabel()
    {
        if (_vm == null) return;
        HolidayLabel.Visibility = _vm.SelectedDayIsHoliday ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        HolidayLabel.Text = _vm.SelectedDayHolidayText;
    }

    // Header buttons
    private void OnPrevClicked(object sender, RoutedEventArgs e)  => _vm?.PreviousMonthCommand.Execute(null);
    private void OnNextClicked(object sender, RoutedEventArgs e)  => _vm?.NextMonthCommand.Execute(null);
    private void OnTodayClicked(object sender, RoutedEventArgs e) => _vm?.GoTodayCommand.Execute(null);

    private void OnNewEventClicked(object sender, RoutedEventArgs e)
    {
        OpenEditWindow(new EventEditParams(StartDate: DateTime.Today.ToString("yyyy-MM-dd")));
    }

    private void OnNewEventForSelectedDayClicked(object sender, RoutedEventArgs e)
    {
        if (_vm?.SelectedDate is DateOnly date)
            OpenEditWindow(new EventEditParams(StartDate: date.ToString("yyyy-MM-dd")));
    }

    private void OnDayCellTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;

        // In WinUI 3 ItemsRepeater with x:Bind, DataContext may not be set.
        // Try DataContext first, then fall back to finding the index in the repeater.
        if (fe.DataContext is CalendarDayCell cell)
        {
            _vm?.SelectDay(cell);
        }
        else
        {
            var index = MonthGrid.GetElementIndex(fe);
            if (index >= 0 && _vm != null && index < _vm.DayCells.Count)
            {
                _vm.SelectDay(_vm.DayCells[index]);
            }
        }
    }

    private void OnCloseSelectedDay(object sender, RoutedEventArgs e)
        => _vm?.CloseSelectedDayCommand.Execute(null);

    private void OnEditEventClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string eventId)
        {
            var occ = _vm?.SelectedDayEvents.FirstOrDefault(x => x.EventId == eventId);
            OpenEditWindow(new EventEditParams(
                EventId: eventId,
                OccurrenceDate: occ?.Date.ToString("yyyy-MM-dd"),
                OccurrenceStartMinute: occ?.StartMinuteOfDay));
        }
    }

    private async void OnDeleteEventClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string eventId) return;

        var item = _vm?.SelectedDayEvents.FirstOrDefault(x => x.EventId == eventId);
        if (item == null) return;

        if (_vm?.IsEventRecurring(eventId) == true)
        {
            var dialog = new ContentDialog
            {
                Title               = AppResources.DeleteEventTitle,
                PrimaryButtonText   = AppResources.DeleteOccurrence,
                SecondaryButtonText = AppResources.DeleteAllOccurrences,
                CloseButtonText     = AppResources.CancelButton,
                XamlRoot            = XamlRoot
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
                _vm?.DeleteOccurrence(eventId, item.OccurrenceKey);
            else if (result == ContentDialogResult.Secondary)
                _vm?.DeleteEntireEvent(eventId);
        }
        else
        {
            var dialog = new ContentDialog
            {
                Title             = AppResources.DeleteEventTitle,
                Content           = item.Title,
                PrimaryButtonText = AppResources.DeleteButton,
                CloseButtonText   = AppResources.CancelButton,
                XamlRoot          = XamlRoot
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
                _vm?.DeleteEntireEvent(eventId);
        }
    }

    // Week view event handlers
    private void OnWeekEmptySlotTapped(object sender, WeekEmptySlotTappedEventArgs e)
    {
        OpenEditWindow(new EventEditParams(
            StartDate: e.Date.ToString("yyyy-MM-dd"),
            StartMinute: e.StartMinute));
    }

    private void OnWeekEventBlockTapped(object sender, WeekEventBlockTappedEventArgs e)
    {
        OpenEditWindow(new EventEditParams(
            EventId: e.EventId,
            OccurrenceDate: e.Date.ToString("yyyy-MM-dd"),
            OccurrenceStartMinute: e.StartMinute));
    }

    private void OnWeekSlotDragCreated(object sender, WeekSlotDragCreatedEventArgs e)
    {
        OpenEditWindow(new EventEditParams(
            StartDate: e.Date.ToString("yyyy-MM-dd"),
            StartMinute: e.StartMinute,
            EndMinute: e.EndMinute));
    }

    private void OpenEditWindow(EventEditParams p)
    {
        var window = new EventEditWindow(p);
        _openEditWindows.Add(window);
        window.Closed += (_, _) => _openEditWindows.Remove(window);
        window.Activate();
    }

    private async void OnWeekEventDragCompleted(object sender, WeekEventDragCompletedEventArgs e)
        => await _interactionCompletionService.HandleDragCompletedAsync(e);

    private async void OnWeekEventResizeCompleted(object sender, WeekEventResizeCompletedEventArgs e)
        => await _interactionCompletionService.HandleResizeCompletedAsync(e);
}
