using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Maui.Devices;
using NolumiaScheduler.Application.Commands;
using NolumiaScheduler.Application.Services;
using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.Repositories;
using NolumiaScheduler.Domain.Services;
using NolumiaScheduler.Domain.ValueObjects;
using NolumiaScheduler.Presentation.Services;
using NolumiaScheduler.Resources.Strings;

namespace NolumiaScheduler.Presentation.ViewModels;

public sealed class CalendarViewModel : INotifyPropertyChanged
{
    private readonly ICalendarEventRepository _events;
    private readonly IBusinessCalendarRepository _businessCalendars;
    private readonly IOccurrenceExpander _expander;
    private readonly CalendarEventApplicationService _eventService;
    private readonly IWeekEventLayoutStrategy _weekEventLayoutStrategy;
    private readonly IWeekAllDayLayoutStrategy _weekAllDayLayoutStrategy;
    private readonly DateTime _today = DateTime.Today.Date;

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
        CalendarEventApplicationService eventService,
        IWeekEventLayoutStrategy weekEventLayoutStrategy,
        IWeekAllDayLayoutStrategy weekAllDayLayoutStrategy)
    {
        _events = events;
        _businessCalendars = businessCalendars;
        _expander = expander;
        _eventService = eventService;
        _weekEventLayoutStrategy = weekEventLayoutStrategy;
        _weekAllDayLayoutStrategy = weekAllDayLayoutStrategy;
        _month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        _weekStartDate = DateTime.Today.AddDays(-(int)DateTime.Today.DayOfWeek);

        DayCells = [];
        SelectedDayEvents = [];
        WeekTimeSlots = [];
        WeekHeaderDays = [];
        WeekDayColumns = [];
        WeekAllDayEventBlocks = [];

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
    public ObservableCollection<WeekDayColumn> WeekDayColumns { get; }
    public ObservableCollection<WeekAllDayEventBlock> WeekAllDayEventBlocks { get; }
    public double WeekAllDayLaneHeight => Math.Max(28, (WeekAllDayEventBlocks.Count == 0 ? 1 : WeekAllDayEventBlocks.Max(x => x.Row + 1)) * 24);
    public double WeekCanvasHeight => 24 * 60;
    public double WeekDayColumnWidth => Math.Max(96, (DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density - 72) / 7);
    public DateTime WeekStartDate => _weekStartDate.Date;
    public bool IsCurrentWeek => _weekStartDate.Date <= DateTime.Now.Date && DateTime.Now.Date <= _weekStartDate.Date.AddDays(6);
    public double CurrentTimeLineTop => (DateTime.Now.Hour * 60) + DateTime.Now.Minute;

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
        LoadMonth();
        LoadWeek();
        if (previousDate != null)
        {
            var newCell = DayCells.FirstOrDefault(c => c.Date.Equals(previousDate));
            if (newCell != null)
                SelectDay(newCell);
        }
    }

    private void Navigate(int step)
    {
        ClearSelection();

        if (IsWeekMode)
        {
            _weekStartDate = _weekStartDate.AddDays(step * 7);
            _month = new DateTime(_weekStartDate.Year, _weekStartDate.Month, 1);
            LoadWeek();
            MonthYearTitle = FormatWeekRangeTitle(_weekStartDate);
            return;
        }

        _month = _month.AddMonths(step);
        _weekStartDate = _month.AddDays(-(int)_month.DayOfWeek);
        LoadMonth();
        LoadWeek();
    }

    private void GoToday()
    {
        ClearSelection();
        _month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        _weekStartDate = DateTime.Today.AddDays(-(int)DateTime.Today.DayOfWeek);
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
            MonthYearTitle = FormatWeekRangeTitle(_weekStartDate);
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
        WeekDayColumns.Clear();
        WeekAllDayEventBlocks.Clear();

        var from = LocalDateValue.FromDateOnly(DateOnly.FromDateTime(_weekStartDate));
        var to = LocalDateValue.FromDateOnly(DateOnly.FromDateTime(_weekStartDate.AddDays(6)));

        var weekly = new Dictionary<int, List<CalendarEventItem>>();
        for (var i = 0; i < 7; i++) weekly[i] = [];

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
            foreach (var occ in _expander.Expand(ev, from, to, calendar))
            {
                var dayIdx = (int)(occ.Date.ToDateOnly().ToDateTime(TimeOnly.MinValue).Date - _weekStartDate.Date).TotalDays;
                if (dayIdx is < 0 or > 6) continue;
                var item = new CalendarEventItem(occ);
                if (!item.IsAllDay && item.CrossesMidnight && dayIdx < 6)
                {
                    var firstOccurrence = new EventOccurrence(
                        occ.EventId,
                        occ.Date,
                        occ.StartTime,
                        new LocalTimeValue(23, 59, 0),
                        false,
                        occ.Title,
                        occ.Location,
                        occ.Visibility,
                        occ.IsMoved,
                        occ.IsOverridden);
                    weekly[dayIdx].Add(new CalendarEventItem(firstOccurrence));

                    var secondOccurrence = new EventOccurrence(
                        occ.EventId,
                        LocalDateValue.FromDateOnly(occ.Date.ToDateOnly().AddDays(1)),
                        new LocalTimeValue(0, 0, 0),
                        occ.EndTime,
                        false,
                        occ.Title,
                        occ.Location,
                        occ.Visibility,
                        occ.IsMoved,
                        occ.IsOverridden);
                    weekly[dayIdx + 1].Add(new CalendarEventItem(secondOccurrence));
                }
                else
                {
                    weekly[dayIdx].Add(item);
                }
            }
        }

        var allDayInput = weekly.SelectMany(x => x.Value).Where(x => x.IsAllDay).ToList();
        foreach (var block in _weekAllDayLayoutStrategy.Layout(allDayInput, _weekStartDate))
            WeekAllDayEventBlocks.Add(block);

        for (var i = 0; i < 7; i++)
        {
            var date = _weekStartDate.AddDays(i);
            var header = date.ToString("ddd M/d", AppResources.FormatCulture);
            WeekHeaderDays.Add(header);
            var isHoliday = _businessCalendars.FindAll().SelectMany(c => c.Holidays).Any(h => h.Date.Equals(LocalDateValue.FromDateOnly(DateOnly.FromDateTime(date))));

            var isToday = date.Date == _today;
            var col = new WeekDayColumn(header, date, isHoliday, isToday);
            for (var h = 0; h < 24; h++)
            {
                col.GuideLines.Add(new HourGuideLine(h));
                col.GuideLines.Add(new HalfHourGuideLine(h));
            }
            foreach (var b in _weekEventLayoutStrategy.Layout(weekly[i])) col.EventBlocks.Add(b);
            WeekDayColumns.Add(col);
        }

        OnPropertyChanged(nameof(WeekCanvasHeight));
        OnPropertyChanged(nameof(WeekStartDate));
        OnPropertyChanged(nameof(WeekAllDayLaneHeight));
    }

    private static string FormatWeekRangeTitle(DateTime weekStartDate)
    {
        var weekEndDate = weekStartDate.AddDays(6);
        return $"{weekStartDate:yyyy/MM/dd (ddd)} - {weekEndDate:yyyy/MM/dd (ddd)}";
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
