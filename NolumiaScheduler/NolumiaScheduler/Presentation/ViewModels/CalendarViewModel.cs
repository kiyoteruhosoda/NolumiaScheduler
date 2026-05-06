using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using NolumiaScheduler.Application.Commands;
using NolumiaScheduler.Application.Services;
using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.Repositories;
using NolumiaScheduler.Domain.Services;
using NolumiaScheduler.Domain.ValueObjects;
using NolumiaScheduler.Resources.Strings;

namespace NolumiaScheduler.Presentation.ViewModels;

public sealed class CalendarViewModel : INotifyPropertyChanged
{
    private readonly ICalendarEventRepository _events;
    private readonly IBusinessCalendarRepository _businessCalendars;
    private readonly IOccurrenceExpander _expander;
    private readonly CalendarEventApplicationService _eventService;

    private DateTime _month;
    private CalendarDayCell? _selectedCell;
    private string _monthYearTitle = "";
    private string _selectedDayLabel = "";
    private bool _hasSelectedDay;
    private bool _selectedDayHasNoEvents;
    private bool _selectedDayIsHoliday;
    private string _selectedDayHolidayText = "";
    private CalendarDisplayMode _displayMode = CalendarDisplayMode.Month;
    private DateTime _weekStartDate;

    public CalendarViewModel(
        ICalendarEventRepository events,
        IBusinessCalendarRepository businessCalendars,
        IOccurrenceExpander expander,
        CalendarEventApplicationService eventService)
    {
        _events = events;
        _businessCalendars = businessCalendars;
        _expander = expander;
        _eventService = eventService;
        _month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        _weekStartDate = DateTime.Today.AddDays(-(int)DateTime.Today.DayOfWeek);

        DayCells = [];
        SelectedDayEvents = [];
        WeekTimeSlots = [];
        WeekHeaderDays = [];

        PreviousMonthCommand = new Command(() => Navigate(-1));
        NextMonthCommand = new Command(() => Navigate(1));
        GoTodayCommand = new Command(GoToday);
        SwitchToMonthViewCommand = new Command(() => SetDisplayMode(CalendarDisplayMode.Month));
        SwitchToWeekViewCommand = new Command(() => SetDisplayMode(CalendarDisplayMode.Week));

        BuildWeekScaffold();
        LoadMonth();
        LoadWeek();
    }

    public ObservableCollection<CalendarDayCell> DayCells { get; }
    public ObservableCollection<CalendarEventItem> SelectedDayEvents { get; }
    public ObservableCollection<WeekTimeSlot> WeekTimeSlots { get; }
    public ObservableCollection<string> WeekHeaderDays { get; }

    public string MonthYearTitle
    {
        get => _monthYearTitle;
        private set { _monthYearTitle = value; OnPropertyChanged(); }
    }

    public string SelectedDayLabel
    {
        get => _selectedDayLabel;
        private set { _selectedDayLabel = value; OnPropertyChanged(); }
    }

    public bool HasSelectedDay
    {
        get => _hasSelectedDay;
        private set { _hasSelectedDay = value; OnPropertyChanged(); }
    }

    public bool SelectedDayHasNoEvents
    {
        get => _selectedDayHasNoEvents;
        private set { _selectedDayHasNoEvents = value; OnPropertyChanged(); }
    }

    public bool SelectedDayIsHoliday
    {
        get => _selectedDayIsHoliday;
        private set { _selectedDayIsHoliday = value; OnPropertyChanged(); }
    }

    public string SelectedDayHolidayText
    {
        get => _selectedDayHolidayText;
        private set { _selectedDayHolidayText = value; OnPropertyChanged(); }
    }

    public ICommand PreviousMonthCommand { get; }
    public ICommand NextMonthCommand { get; }
    public ICommand GoTodayCommand { get; }
    public ICommand SwitchToMonthViewCommand { get; }
    public ICommand SwitchToWeekViewCommand { get; }


    public CalendarDisplayMode DisplayMode
    {
        get => _displayMode;
        private set
        {
            if (_displayMode == value) return;
            _displayMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsMonthMode));
            OnPropertyChanged(nameof(IsWeekMode));
        }
    }

    public bool IsMonthMode => DisplayMode == CalendarDisplayMode.Month;
    public bool IsWeekMode => DisplayMode == CalendarDisplayMode.Week;

    public void SelectDay(CalendarDayCell cell)
    {
        _selectedCell?.IsSelected = false;

        _selectedCell = cell;
        cell.IsSelected = true;

        var date = cell.Date.ToDateOnly();
        SelectedDayLabel = date.ToString(AppResources.SelectedDayFormat, AppResources.FormatCulture);
        HasSelectedDay = true;

        SelectedDayEvents.Clear();
        foreach (var occ in cell.Events)
            SelectedDayEvents.Add(new CalendarEventItem(occ));
        SelectedDayHasNoEvents = SelectedDayEvents.Count == 0;

        // Collect holiday info for the selected day across all business calendars
        if (cell.IsHoliday)
        {
            var parts = new List<string>();
            foreach (var cal in _businessCalendars.FindAll())
            {
                var h = cal.Holidays.FirstOrDefault(h => h.Date.Equals(cell.Date));
                if (h != null)
                {
                    var entry = string.IsNullOrEmpty(h.Name) ? cal.Name : $"{cal.Name}：{h.Name}";
                    parts.Add(entry);
                }
            }
            SelectedDayIsHoliday = true;
            SelectedDayHolidayText = string.Join("  /  ", parts);
        }
        else
        {
            SelectedDayIsHoliday = false;
            SelectedDayHolidayText = "";
        }
    }

    public void ReloadCurrentMonth() => RefreshAfterChange();

    public void DeleteEntireEvent(string eventId)
    {
        _eventService.DeleteEvent(eventId);
        RefreshAfterChange();
    }

    public void DeleteOccurrence(string eventId, OccurrenceLocalKey key)
    {
        _eventService.DeleteOccurrence(new SkipOccurrenceCommand(eventId, key));
        RefreshAfterChange();
    }

    private void RefreshAfterChange()
    {
        var previousDate = _selectedCell?.Date;
        BuildWeekScaffold();
        LoadMonth();
        LoadWeek();
        if (previousDate != null)
        {
            var newCell = DayCells.FirstOrDefault(c => c.Date.Equals(previousDate));
            if (newCell != null)
                SelectDay(newCell);
        }
    }

    private void Navigate(int months)
    {
        ClearSelection();
        _month = _month.AddMonths(months);
        BuildWeekScaffold();
        LoadMonth();
        LoadWeek();
    }

    private void GoToday()
    {
        ClearSelection();
        _month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        _weekStartDate = DateTime.Today.AddDays(-(int)DateTime.Today.DayOfWeek);
        BuildWeekScaffold();
        LoadMonth();
        LoadWeek();
    }

    private void ClearSelection()
    {
        _selectedCell?.IsSelected = false;
        _selectedCell = null;
        HasSelectedDay = false;
        SelectedDayHasNoEvents = false;
        SelectedDayIsHoliday = false;
        SelectedDayHolidayText = "";
        SelectedDayEvents.Clear();
    }

    private void SetDisplayMode(CalendarDisplayMode mode)
    {
        DisplayMode = mode;
        if (mode == CalendarDisplayMode.Month)
        {
            MonthYearTitle = _month.ToString(AppResources.MonthYearFormat, AppResources.FormatCulture);
        }
        else
        {
            MonthYearTitle = $"{_weekStartDate:yyyy年M月d日} - {_weekStartDate.AddDays(6):M月d日}";
        }
    }

    private void BuildWeekScaffold()
    {
        WeekTimeSlots.Clear();
        for (var h = 0; h < 24; h++) WeekTimeSlots.Add(new WeekTimeSlot(h));
    }

    private void LoadWeek()
    {
        WeekHeaderDays.Clear();
        for (var i = 0; i < 7; i++)
        {
            var d = _weekStartDate.AddDays(i);
            WeekHeaderDays.Add(d.ToString("M/d(ddd)", AppResources.FormatCulture));
        }
    }

    private void LoadMonth()
    {
        MonthYearTitle = _month.ToString(AppResources.MonthYearFormat, AppResources.FormatCulture);

        var today = LocalDateValue.FromDateOnly(DateOnly.FromDateTime(DateTime.Today));
        var monthFrom = LocalDateValue.FromDateOnly(DateOnly.FromDateTime(_month));
        var monthTo = LocalDateValue.FromDateOnly(
            DateOnly.FromDateTime(_month.AddMonths(1).AddDays(-1)));

        var byDate = new Dictionary<string, List<EventOccurrence>>();

        // Build a lookup of business calendars for occurrence expansion (holiday shifting)
        var calendarCache = new Dictionary<string, BusinessCalendar>();
        BusinessCalendar? GetCalendar(string? id)
        {
            if (id == null) return null;
            if (!calendarCache.TryGetValue(id, out var cal))
                calendarCache[id] = cal = _businessCalendars.FindById(new BusinessCalendarId(id))!;
            return cal;
        }

        foreach (var ev in _events.FindAll())
        {
            var calId = ev.RecurringSchedule?.RecurrenceRule.Adjustment?.CalendarId?.Value;
            var calendar = GetCalendar(calId);

            foreach (var occ in _expander.Expand(ev, monthFrom, monthTo, calendar))
            {
                var key = occ.Date.ToString();
                if (!byDate.TryGetValue(key, out var list))
                    byDate[key] = list = [];
                list.Add(occ);
            }
        }

        // Sort: all-day first, then by start time
        foreach (var list in byDate.Values)
        {
            list.Sort((a, b) =>
            {
                if (a.AllDay != b.AllDay) return a.AllDay ? -1 : 1;
                if (a.StartTime != null && b.StartTime != null)
                    return a.StartTime.CompareTo(b.StartTime);
                return 0;
            });
        }

        // Collect holidays from all business calendars for display
        var holidayByDate = new Dictionary<string, string?>();
        foreach (var cal in _businessCalendars.FindAll())
        {
            foreach (var h in cal.Holidays)
            {
                var key = h.Date.ToString();
                if (!holidayByDate.ContainsKey(key))
                    holidayByDate[key] = h.Name;
            }
        }

        DayCells.Clear();

        // Grid starts from the Sunday of the week containing the 1st of the month
        var gridStart = _month.AddDays(-(int)_month.DayOfWeek);

        for (var i = 0; i < 42; i++)
        {
            var date = gridStart.AddDays(i);
            var dateVal = LocalDateValue.FromDateOnly(DateOnly.FromDateTime(date));
            byDate.TryGetValue(dateVal.ToString(), out var evts);
            holidayByDate.TryGetValue(dateVal.ToString(), out var holidayName);
            var isHoliday = holidayByDate.ContainsKey(dateVal.ToString());

            DayCells.Add(new CalendarDayCell
            {
                Date = dateVal,
                IsToday = dateVal.Equals(today),
                IsCurrentMonth = date.Month == _month.Month,
                Events = (IReadOnlyList<EventOccurrence>?)evts ?? [],
                IsHoliday = isHoliday,
                HolidayName = holidayName,
            });
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
