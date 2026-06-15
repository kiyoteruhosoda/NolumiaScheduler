using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using NolumiaScheduler.Application.Commands;
using NolumiaScheduler.Application.Services;
using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.Services;
using NolumiaScheduler.Domain.ValueObjects;
using NolumiaScheduler.Presentation.Services;
using NolumiaScheduler.Presentation.Helpers;
using NolumiaScheduler.Presentation.Resources.Strings;

namespace NolumiaScheduler.Presentation.ViewModels;

public partial class CalendarViewModel : INotifyPropertyChanged
{
    private readonly CalendarEventApplicationService _eventService;
    private readonly BusinessCalendarApplicationService _calendarService;
    private readonly IOccurrenceExpander _expander;
    private readonly IWeekEventLayoutStrategy _weekEventLayoutStrategy;
    private readonly IWeekAllDayLayoutStrategy _weekAllDayLayoutStrategy;
    private readonly TimeProvider _timeProvider;
    private DateTime _today;

    private DateTime _month;
    private CalendarDayCell? _selectedCell;
    private string _monthYearTitle = "";
    private string _selectedDayLabel = "";
    private bool _hasSelectedDay;
    private bool _selectedDayHasNoEvents;
    private bool _selectedDayIsHoliday;
    private string _selectedDayHolidayText = "";
    private CalendarDisplayMode _displayMode = CalendarDisplayMode.Week;
    private DateTime _weekStartDate;

    public CalendarViewModel(
        CalendarEventApplicationService eventService,
        BusinessCalendarApplicationService calendarService,
        IOccurrenceExpander expander,
        IWeekEventLayoutStrategy weekEventLayoutStrategy,
        IWeekAllDayLayoutStrategy weekAllDayLayoutStrategy,
        TimeProvider timeProvider)
    {
        _eventService = eventService;
        _calendarService = calendarService;
        _expander = expander;
        _weekEventLayoutStrategy = weekEventLayoutStrategy;
        _weekAllDayLayoutStrategy = weekAllDayLayoutStrategy;
        _timeProvider = timeProvider;
        var today = Today;
        _today = today;
        _month = new DateTime(today.Year, today.Month, 1);
        _weekStartDate = today.AddDays(-(int)today.DayOfWeek);

        DayCells = [];
        SelectedDayEvents = [];
        WeekTimeSlots = [];
        WeekHeaderDays = [];
        WeekDayColumns = [];
        WeekAllDayEventBlocks = [];

        PreviousMonthCommand = new RelayCommand(() => Navigate(-1));
        NextMonthCommand = new RelayCommand(() => Navigate(1));
        GoTodayCommand = new RelayCommand(GoToday);
        SwitchToMonthViewCommand = new RelayCommand(() => SetDisplayMode(CalendarDisplayMode.Month));
        SwitchToWeekViewCommand = new RelayCommand(() => SetDisplayMode(CalendarDisplayMode.Week));
        SwitchToWeekdaysViewCommand = new RelayCommand(() => SetDisplayMode(CalendarDisplayMode.Weekdays));
        CloseSelectedDayCommand = new RelayCommand(ClearSelection);

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
    public static double WeekCanvasHeight => 24 * 60;
    public static double WeekDayColumnWidth => 120;
    public DateTime WeekStartDate => _weekStartDate.Date;

    private double _dayCellHeight = 88;
    public double DayCellHeight
    {
        get => _dayCellHeight;
        set
        {
            if (Math.Abs(_dayCellHeight - value) < 0.5) return;
            _dayCellHeight = value;
            OnPropertyChanged();
            UpdateDayCellChipCount();
        }
    }

    private void UpdateDayCellChipCount()
    {
        // Header (day-number circle + margins + grid padding) ~= 36px; each chip row is
        // 14px tall with 2px spacing => 16px. Max chips that fit without the "+N" label.
        var chipCount = Math.Max(1, (int)((_dayCellHeight - 36) / 16));
        foreach (var cell in DayCells)
        {
            cell.AvailableChipCount = chipCount;
            cell.CellHeight = _dayCellHeight;
        }
    }
    // Wall-clock readers go through the injected TimeProvider so the calendar's notion of "now"
    // and "today" can be controlled deterministically in tests.
    private DateTime Now => _timeProvider.GetLocalNow().DateTime;
    private DateTime Today => _timeProvider.GetLocalNow().Date;

    public bool IsCurrentWeek => WeekContains(Today);

    // True when the given date falls inside the currently displayed week window. Weeks run
    // Sun..Sat internally (and the weekdays view's _weekStartDate is the Monday of that same
    // week), so a seven-day window from _weekStartDate covers both view modes.
    private bool WeekContains(DateTime date) =>
        _weekStartDate.Date <= date.Date && date.Date <= _weekStartDate.Date.AddDays(6);
    public double CurrentTimeLineTop => (Now.Hour * 60) + Now.Minute;

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
        private set { _hasSelectedDay = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsMonthModeAndHasSelectedDay)); }
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

    public DateOnly? SelectedDate => _selectedCell?.Date.ToDateOnly();

    public ICommand PreviousMonthCommand { get; }
    public ICommand NextMonthCommand { get; }
    public ICommand GoTodayCommand { get; }
    public ICommand SwitchToMonthViewCommand { get; }
    public ICommand SwitchToWeekViewCommand { get; }
    public ICommand SwitchToWeekdaysViewCommand { get; }
    public ICommand CloseSelectedDayCommand { get; }


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
            OnPropertyChanged(nameof(IsWeekdaysMode));
            OnPropertyChanged(nameof(IsMonthModeAndHasSelectedDay));
        }
    }

    public bool IsMonthMode => DisplayMode == CalendarDisplayMode.Month;
    public bool IsWeekMode => DisplayMode == CalendarDisplayMode.Week;
    public bool IsWeekdaysMode => DisplayMode == CalendarDisplayMode.Weekdays;
    public bool IsMonthModeAndHasSelectedDay => IsMonthMode && HasSelectedDay;

    public void SelectDay(CalendarDayCell cell)
    {
        _selectedCell?.IsSelected = false;

        _selectedCell = cell;
        cell.IsSelected = true;

        var date = cell.Date.ToDateOnly();
        SelectedDayLabel = date.ToString(AppResources.SelectedDayFormat, AppResources.FormatCulture);
        HasSelectedDay = true;
        OnPropertyChanged(nameof(SelectedDate));

        SelectedDayEvents.Clear();
        foreach (var occ in cell.Events)
            SelectedDayEvents.Add(new CalendarEventItem(occ));
        SelectedDayHasNoEvents = SelectedDayEvents.Count == 0;

        // Collect holiday info for the selected day across all business calendars
        if (cell.IsHoliday)
        {
            var parts = new List<string>();
            foreach (var cal in _calendarService.FindAll())
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

    /// <summary>
    /// Re-evaluates wall-clock-dependent state so the view keeps up while the app is left
    /// running. The "now" line moves every minute; on a midnight rollover the "today"
    /// highlight has to move to the new day and the current-week test re-run. Driven by a
    /// periodic timer in the hosting page (no clock ticks on its own here).
    /// </summary>
    public void RefreshCurrentTime()
    {
        // The "now" line position depends on the current time of day.
        OnPropertyChanged(nameof(CurrentTimeLineTop));

        var today = Today;
        if (_today == today) return;

        // The date changed (e.g. the app stayed open past midnight).
        var previousToday = _today;
        _today = today;

        // In the week / weekdays view, when the display is still following the current week, a
        // midnight rollover that crosses into a new week should advance the view to the new
        // week so "today" stays visible. If the user has navigated to some other week we leave
        // their position alone and only refresh the "today" markers below.
        if ((IsWeekMode || IsWeekdaysMode) && WeekContains(previousToday))
        {
            var newWeekStart = IsWeekdaysMode
                ? AlignToMonday(today)
                : today.AddDays(-(int)today.DayOfWeek);
            if (newWeekStart.Date != _weekStartDate.Date)
            {
                ClearSelection();
                _weekStartDate = newWeekStart;
                _month = new DateTime(_weekStartDate.Year, _weekStartDate.Month, 1);
                LoadMonth();
                LoadWeek();
                MonthYearTitle = IsWeekdaysMode
                    ? FormatWeekdaysRangeTitle(_weekStartDate)
                    : FormatWeekRangeTitle(_weekStartDate);
                return;
            }
        }

        // The date changed but the displayed week did not (or we're in month mode): rebuild so
        // the "today" highlight lands on the new day. This keeps the current view in place — it
        // only refreshes which cell/column is marked as today, not where the user navigated to.
        RefreshAfterChange();
    }

    public bool IsEventRecurring(string eventId) =>
        _eventService.FindById(eventId)?.IsRecurring() ?? false;

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

    // Confirms a week-view drag (move): keeps the event duration and applies the new
    // day + start time directly. Recurring events detach only the dragged occurrence.
    public void MoveEventOccurrence(
        string eventId, OccurrenceLocalKey? occurrenceKey,
        DateOnly newDate, int newStartMinute, int durationMinutes)
    {
        var duration = Math.Clamp(durationMinutes, 15, MinutesPerDay - 15);
        var startMinute = Math.Clamp(newStartMinute, 0, MinutesPerDay - 15);
        var endMinute = Math.Min(startMinute + duration, MinutesPerDay - 1);
        ApplyOccurrenceReschedule(eventId, occurrenceKey, newDate, startMinute, endMinute);
    }

    // Confirms a week-view resize: applies the new start/end time directly on the same day.
    public void ResizeEventOccurrence(
        string eventId, OccurrenceLocalKey? occurrenceKey,
        DateOnly date, int startMinute, int endMinute)
    {
        var start = Math.Clamp(startMinute, 0, MinutesPerDay - 15);
        var end = Math.Clamp(endMinute, start + 15, MinutesPerDay - 1);
        ApplyOccurrenceReschedule(eventId, occurrenceKey, date, start, end);
    }

    private const int MinutesPerDay = 24 * 60;

    private void ApplyOccurrenceReschedule(
        string eventId, OccurrenceLocalKey? occurrenceKey,
        DateOnly date, int startMinute, int endMinute)
    {
        var ev = _eventService.FindById(eventId);
        if (ev == null) return;

        try
        {
            if (ev.IsRecurring())
            {
                if (occurrenceKey == null) return;
                _eventService.MoveOccurrence(new MoveOccurrenceCommand(
                    eventId,
                    occurrenceKey,
                    LocalDateValue.FromDateOnly(date),
                    ToLocalTime(startMinute),
                    ToLocalTime(endMinute),
                    null, null, null));
            }
            else
            {
                _eventService.UpdateEvent(new UpdateEventCommand(
                    eventId,
                    ev.Title.Value,
                    ev.Location?.Value,
                    ev.Visibility,
                    false,
                    date,
                    TimeSpan.FromMinutes(startMinute),
                    TimeSpan.FromMinutes(endMinute),
                    ev.Alarm));
            }
            RefreshAfterChange();
        }
        catch (Exception)
        {
            // Invalid reschedule (e.g. dragging an already-overridden occurrence): leave the
            // event unchanged so the chip snaps back to its original position.
        }
    }

    private static LocalTimeValue ToLocalTime(int minuteOfDay)
        => new(minuteOfDay / 60, minuteOfDay % 60, 0);

    private void RefreshAfterChange()
    {
        var previousDate = _selectedCell?.Date;
        LoadMonth();
        LoadWeek();
        // LoadMonth always writes the month-format title; restore the week title when in week mode.
        if (IsWeekMode)
            MonthYearTitle = FormatWeekRangeTitle(_weekStartDate);
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

        if (IsWeekdaysMode)
        {
            _weekStartDate = _weekStartDate.AddDays(step * 7);
            _month = new DateTime(_weekStartDate.Year, _weekStartDate.Month, 1);
            LoadWeek();
            MonthYearTitle = FormatWeekdaysRangeTitle(_weekStartDate);
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
        var today = Today;
        _month = new DateTime(today.Year, today.Month, 1);
        _weekStartDate = IsWeekdaysMode
            ? AlignToMonday(today)
            : today.AddDays(-(int)today.DayOfWeek);
        LoadMonth();
        LoadWeek();
        if (IsWeekMode)
            MonthYearTitle = FormatWeekRangeTitle(_weekStartDate);
        else if (IsWeekdaysMode)
            MonthYearTitle = FormatWeekdaysRangeTitle(_weekStartDate);
    }

    private void ClearSelection()
    {
        _selectedCell?.IsSelected = false;
        _selectedCell = null;
        HasSelectedDay = false;
        OnPropertyChanged(nameof(SelectedDate));
        SelectedDayHasNoEvents = false;
        SelectedDayIsHoliday = false;
        SelectedDayHolidayText = "";
        SelectedDayEvents.Clear();
    }

    private void SetDisplayMode(CalendarDisplayMode mode)
    {
        if (mode == CalendarDisplayMode.Weekdays && _displayMode != CalendarDisplayMode.Weekdays)
        {
            _weekStartDate = AlignToMonday(_weekStartDate);
        }
        DisplayMode = mode;
        if (mode == CalendarDisplayMode.Month)
        {
            MonthYearTitle = _month.ToString(AppResources.MonthYearFormat, AppResources.FormatCulture);
        }
        else if (mode == CalendarDisplayMode.Weekdays)
        {
            LoadWeek();
            MonthYearTitle = FormatWeekdaysRangeTitle(_weekStartDate);
            return;
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

        var dayCount = IsWeekdaysMode ? 5 : 7;
        var from = LocalDateValue.FromDateOnly(DateOnly.FromDateTime(_weekStartDate));
        var to = LocalDateValue.FromDateOnly(DateOnly.FromDateTime(_weekStartDate.AddDays(dayCount - 1)));

        var weekly = new Dictionary<int, List<CalendarEventItem>>();
        for (var i = 0; i < dayCount; i++) weekly[i] = [];

        var calendarCache = new Dictionary<string, BusinessCalendar>();
        BusinessCalendar? GetCalendar(string? id)
        {
            if (id == null) return null;
            if (!calendarCache.TryGetValue(id, out var cal))
            {
                var found = _calendarService.FindById(id);
                if (found != null) calendarCache[id] = cal = found;
            }
            return cal;
        }

        foreach (var ev in _eventService.FindAll())
        {
            var calId = ev.RecurringSchedule?.RecurrenceRule.Adjustment?.CalendarId?.Value;
            var calendar = GetCalendar(calId);
            foreach (var occ in _expander.Expand(ev, from, to, calendar))
            {
                var dayIdx = (int)(occ.Date.ToDateOnly().ToDateTime(TimeOnly.MinValue).Date - _weekStartDate.Date).TotalDays;
                if (dayIdx < 0 || dayIdx >= dayCount) continue;
                var item = new CalendarEventItem(occ);
                if (!item.IsAllDay && item.CrossesMidnight && dayIdx < dayCount - 1)
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
                        occ.IsOverridden,
                        occ.SeriesKey,
                        occ.ColorKey);
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
                        occ.IsOverridden,
                        occ.SeriesKey,
                        occ.ColorKey);
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

        for (var i = 0; i < dayCount; i++)
        {
            var date = _weekStartDate.AddDays(i);
            var header = date.ToString("ddd M/d", AppResources.FormatCulture);
            WeekHeaderDays.Add(header);
            var isHoliday = _calendarService.FindAll().SelectMany(c => c.Holidays).Any(h => h.Date.Equals(LocalDateValue.FromDateOnly(DateOnly.FromDateTime(date))));

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
        OnPropertyChanged(nameof(IsCurrentWeek));
        OnPropertyChanged(nameof(WeekStartDate));
        OnPropertyChanged(nameof(WeekAllDayLaneHeight));
    }

    // Weeks run Sun..Sat internally; the weekdays view shows the Mon..Fri inside that
    // same week, so a Sunday start moves FORWARD one day (not back to last week's Monday).
    private static DateTime AlignToMonday(DateTime date) =>
        date.DayOfWeek == DayOfWeek.Sunday
            ? date.AddDays(1)
            : date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    private static string FormatWeekdaysRangeTitle(DateTime weekStartDate)
    {
        var weekEndDate = weekStartDate.AddDays(4);
        return $"{weekStartDate:yyyy/MM/dd (ddd)} - {weekEndDate:yyyy/MM/dd (ddd)}";
    }

    private static string FormatWeekRangeTitle(DateTime weekStartDate)
    {
        var weekEndDate = weekStartDate.AddDays(6);
        return $"{weekStartDate:yyyy/MM/dd (ddd)} - {weekEndDate:yyyy/MM/dd (ddd)}";
    }

    private void LoadMonth()
    {
        MonthYearTitle = _month.ToString(AppResources.MonthYearFormat, AppResources.FormatCulture);

        var today = LocalDateValue.FromDateOnly(DateOnly.FromDateTime(Today));
        var monthFrom = LocalDateValue.FromDateOnly(DateOnly.FromDateTime(_month));
        var monthTo = LocalDateValue.FromDateOnly(
            DateOnly.FromDateTime(_month.AddMonths(1).AddDays(-1)));

        var byDate = new Dictionary<string, List<EventOccurrence>>();

        var calendarCache = new Dictionary<string, BusinessCalendar>();
        BusinessCalendar? GetCalendar(string? id)
        {
            if (id == null) return null;
            if (!calendarCache.TryGetValue(id, out var cal))
            {
                var found = _calendarService.FindById(id);
                if (found != null) calendarCache[id] = cal = found;
            }
            return cal;
        }

        foreach (var ev in _eventService.FindAll())
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
        foreach (var cal in _calendarService.FindAll())
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

        UpdateDayCellChipCount();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
