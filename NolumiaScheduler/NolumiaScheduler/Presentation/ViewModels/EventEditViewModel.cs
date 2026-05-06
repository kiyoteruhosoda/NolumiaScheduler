using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using NolumiaScheduler.Application.Commands;
using NolumiaScheduler.Application.Services;
using NolumiaScheduler.Domain.Repositories;
using NolumiaScheduler.Domain.ValueObjects;
using NolumiaScheduler.Resources.Strings;

namespace NolumiaScheduler.Presentation.ViewModels;

/// <summary>Repeat type selection index: 0=None 1=Weekly 2=Monthly 3=Yearly</summary>
public enum RepeatTypeIndex { None = 0, Weekly = 1, Monthly = 2, Yearly = 3 }

/// <summary>Monthly rule index: 0=DayOfMonth 1=NthWeekday</summary>
public enum MonthlyRuleIndex { DayOfMonth = 0, NthWeekday = 1 }

/// <summary>Adjustment index: 0=None 1=Forward 2=Backward</summary>
public enum AdjustmentIndex { None = 0, Forward = 1, Backward = 2 }

public sealed class EventEditViewModel : INotifyPropertyChanged
{
    private readonly CalendarEventApplicationService _eventService;
    private readonly IBusinessCalendarRepository _calendarRepo;

    // ── Basic fields ──────────────────────────────────────────
    private string _title = "";
    private string _location = "";
    private bool _allDay;
    private DateTime _startDate = DateTime.Today;
    private TimeSpan _startTime = new(9, 0, 0);
    private TimeSpan _endTime = new(10, 0, 0);

    // ── Recurrence ───────────────────────────────────────────
    private int _repeatTypeIndex;
    private int _interval = 1;
    private DateTime _endDate = DateTime.Today.AddYears(1);

    // Weekly
    private bool _weekSun, _weekMon, _weekTue, _weekWed, _weekThu, _weekFri, _weekSat;

    // Monthly
    private int _monthlyRuleIndex;
    private int _dayOfMonth = 1;
    private int _weekIndexPickerIndex = 1;   // maps: 0→1st, 1→2nd, 2→3rd, 3→4th, 4→5th, 5→Last
    private int _monthlyWeekdayIndex;         // 0=Sun … 6=Sat

    // Yearly
    private int _yearlyRuleIndex;             // 0=DayOfMonth 1=NthWeekday
    private int _yearlyMonth = 1;
    private int _yearlyDay = 1;
    private int _yearlyWeekIndexPickerIndex = 1;
    private int _yearlyWeekdayIndex;

    // Adjustment
    private int _adjustmentIndex;
    private int _selectedCalendarIndex = -1;

    private string _validationError = "";

    // ── Constructor ──────────────────────────────────────────
    public EventEditViewModel(
        CalendarEventApplicationService eventService,
        IBusinessCalendarRepository calendarRepo)
    {
        _eventService = eventService;
        _calendarRepo = calendarRepo;

        SaveCommand = new Command(Save);

        // Default: Mon selected for weekly
        _weekMon = true;

        LoadAvailableCalendars();
    }

    // ── Picker item lists ─────────────────────────────────────

    public List<string> RepeatTypeItems =>
    [
        AppResources.RepeatNone,
        AppResources.RepeatWeekly,
        AppResources.RepeatMonthly,
        AppResources.RepeatYearly
    ];

    public List<string> MonthlyRuleItems =>
    [
        AppResources.MonthlyDayOfMonth,
        AppResources.MonthlyNthWeekday
    ];

    public List<string> YearlyRuleItems => MonthlyRuleItems;

    public List<string> WeekIndexItems =>
    [
        "1st", "2nd", "3rd", "4th", "5th", AppResources.NthWeekLast
    ];

    public List<string> WeekdayItems =>
    [
        AppResources.DaySun, AppResources.DayMon, AppResources.DayTue,
        AppResources.DayWed, AppResources.DayThu, AppResources.DayFri, AppResources.DaySat
    ];

    public List<string> AdjustmentItems =>
    [
        AppResources.AdjustmentNone,
        AppResources.AdjustmentForward,
        AppResources.AdjustmentBackward
    ];

    public ObservableCollection<string> AvailableCalendarNames { get; } = [];
    private List<string> _availableCalendarIds = [];

    // ── Properties ────────────────────────────────────────────

    public string Title
    {
        get => _title;
        set { _title = value; OnPropertyChanged(); }
    }

    public string Location
    {
        get => _location;
        set { _location = value; OnPropertyChanged(); }
    }

    public bool AllDay
    {
        get => _allDay;
        set
        {
            _allDay = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowTimeSection));
        }
    }

    public DateTime StartDate
    {
        get => _startDate;
        set { _startDate = value; OnPropertyChanged(); }
    }

    public TimeSpan StartTime
    {
        get => _startTime;
        set { _startTime = value; OnPropertyChanged(); }
    }

    public TimeSpan EndTime
    {
        get => _endTime;
        set { _endTime = value; OnPropertyChanged(); }
    }

    // ── Recurrence ───────────────────────────────────────────

    public int RepeatTypeIndex
    {
        get => _repeatTypeIndex;
        set
        {
            _repeatTypeIndex = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsRecurring));
            OnPropertyChanged(nameof(IsWeekly));
            OnPropertyChanged(nameof(IsMonthly));
            OnPropertyChanged(nameof(IsYearly));
            OnPropertyChanged(nameof(IntervalUnitLabel));
        }
    }

    public int Interval
    {
        get => _interval;
        set { _interval = Math.Max(1, value); OnPropertyChanged(); }
    }

    public DateTime EndDate
    {
        get => _endDate;
        set { _endDate = value; OnPropertyChanged(); }
    }

    // Weekly
    public bool WeekSun    { get => _weekSun;    set { _weekSun    = value; OnPropertyChanged(); } }
    public bool WeekMon    { get => _weekMon;    set { _weekMon    = value; OnPropertyChanged(); } }
    public bool WeekTue    { get => _weekTue;    set { _weekTue    = value; OnPropertyChanged(); } }
    public bool WeekWed    { get => _weekWed;    set { _weekWed    = value; OnPropertyChanged(); } }
    public bool WeekThu    { get => _weekThu;    set { _weekThu    = value; OnPropertyChanged(); } }
    public bool WeekFri    { get => _weekFri;    set { _weekFri    = value; OnPropertyChanged(); } }
    public bool WeekSat    { get => _weekSat;    set { _weekSat    = value; OnPropertyChanged(); } }

    // Monthly
    public int MonthlyRuleIndex
    {
        get => _monthlyRuleIndex;
        set
        {
            _monthlyRuleIndex = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsMonthlyDayOfMonth));
            OnPropertyChanged(nameof(IsMonthlyNthWeekday));
        }
    }

    public int DayOfMonth
    {
        get => _dayOfMonth;
        set { _dayOfMonth = Math.Clamp(value, 1, 31); OnPropertyChanged(); }
    }

    public int WeekIndexPickerIndex
    {
        get => _weekIndexPickerIndex;
        set { _weekIndexPickerIndex = value; OnPropertyChanged(); }
    }

    public int MonthlyWeekdayIndex
    {
        get => _monthlyWeekdayIndex;
        set { _monthlyWeekdayIndex = value; OnPropertyChanged(); }
    }

    // Yearly
    public int YearlyRuleIndex
    {
        get => _yearlyRuleIndex;
        set
        {
            _yearlyRuleIndex = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsYearlyDayOfMonth));
            OnPropertyChanged(nameof(IsYearlyNthWeekday));
        }
    }

    public int YearlyMonth
    {
        get => _yearlyMonth;
        set { _yearlyMonth = Math.Clamp(value, 1, 12); OnPropertyChanged(); }
    }

    public int YearlyDay
    {
        get => _yearlyDay;
        set { _yearlyDay = Math.Clamp(value, 1, 31); OnPropertyChanged(); }
    }

    public int YearlyWeekIndexPickerIndex
    {
        get => _yearlyWeekIndexPickerIndex;
        set { _yearlyWeekIndexPickerIndex = value; OnPropertyChanged(); }
    }

    public int YearlyWeekdayIndex
    {
        get => _yearlyWeekdayIndex;
        set { _yearlyWeekdayIndex = value; OnPropertyChanged(); }
    }

    // Adjustment
    public int AdjustmentIndex
    {
        get => _adjustmentIndex;
        set
        {
            _adjustmentIndex = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasAdjustment));
        }
    }

    public int SelectedCalendarIndex
    {
        get => _selectedCalendarIndex;
        set { _selectedCalendarIndex = value; OnPropertyChanged(); }
    }

    // ── Computed visibility ──────────────────────────────────

    public bool ShowTimeSection    => !AllDay;
    public bool IsRecurring        => _repeatTypeIndex != (int)ViewModels.RepeatTypeIndex.None;
    public bool IsWeekly           => _repeatTypeIndex == (int)ViewModels.RepeatTypeIndex.Weekly;
    public bool IsMonthly          => _repeatTypeIndex == (int)ViewModels.RepeatTypeIndex.Monthly;
    public bool IsYearly           => _repeatTypeIndex == (int)ViewModels.RepeatTypeIndex.Yearly;
    public bool IsMonthlyDayOfMonth  => _monthlyRuleIndex == (int)MonthlyRuleIndex.DayOfMonth;
    public bool IsMonthlyNthWeekday  => _monthlyRuleIndex == (int)MonthlyRuleIndex.NthWeekday;
    public bool IsYearlyDayOfMonth   => _yearlyRuleIndex == 0;
    public bool IsYearlyNthWeekday   => _yearlyRuleIndex == 1;
    public bool HasAdjustment        => _adjustmentIndex != (int)ViewModels.AdjustmentIndex.None;
    public bool HasAvailableCalendars => AvailableCalendarNames.Count > 0;

    public string IntervalUnitLabel => _repeatTypeIndex switch
    {
        (int)ViewModels.RepeatTypeIndex.Weekly  => AppResources.RepeatWeekly,
        (int)ViewModels.RepeatTypeIndex.Monthly => AppResources.RepeatMonthly,
        (int)ViewModels.RepeatTypeIndex.Yearly  => AppResources.RepeatYearly,
        _ => ""
    };

    public string ValidationError
    {
        get => _validationError;
        private set { _validationError = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasValidationError)); }
    }

    public bool HasValidationError => !string.IsNullOrEmpty(_validationError);

    public ICommand SaveCommand { get; }

    public event Action? SaveCompleted;

    // ── Save logic ────────────────────────────────────────────

    private void Save()
    {
        ValidationError = "";

        if (string.IsNullOrWhiteSpace(Title))
        {
            ValidationError = AppResources.ErrorTitleRequired;
            return;
        }

        try
        {
            if (!IsRecurring)
                SaveSingle();
            else
                SaveRecurring();

            if (string.IsNullOrEmpty(ValidationError))
                SaveCompleted?.Invoke();
        }
        catch (Exception ex)
        {
            ValidationError = ex.Message;
        }
    }

    private void SaveSingle()
    {
        var tz = "Asia/Tokyo";
        var tzInfo = TimeZoneInfo.FindSystemTimeZoneById(tz);
        DateTimeOffset start, end;

        if (AllDay)
        {
            var offset = tzInfo.GetUtcOffset(StartDate);
            start = new DateTimeOffset(StartDate.Year, StartDate.Month, StartDate.Day, 0, 0, 0, offset);
            end = start.AddDays(1);
        }
        else
        {
            var startDt = StartDate.Date + StartTime;
            var endDt   = StartDate.Date + EndTime;
            var offset  = tzInfo.GetUtcOffset(startDt);
            start = new DateTimeOffset(startDt, offset);
            end   = new DateTimeOffset(endDt, offset);
        }

        _eventService.CreateSingleEvent(new CreateSingleEventCommand(
            Title.Trim(),
            string.IsNullOrWhiteSpace(Location) ? null : Location.Trim(),
            Visibility.Public,
            null, null,
            tz,
            AllDay,
            start, end));
    }

    private void SaveRecurring()
    {
        if (IsWeekly)
        {
            var selectedDays = CollectWeekdays();
            if (selectedDays.Count == 0)
            {
                ValidationError = AppResources.ErrorWeekdayRequired;
                return;
            }
        }

        var rule = BuildRecurrenceRule();
        var startDate = new LocalDateValue(StartDate.Year, StartDate.Month, StartDate.Day);
        LocalTimeValue? startTime = AllDay ? null : new LocalTimeValue(StartTime.Hours, StartTime.Minutes, 0);
        LocalTimeValue? endTime   = AllDay ? null : new LocalTimeValue(EndTime.Hours,   EndTime.Minutes,   0);

        _eventService.CreateRecurringEvent(new CreateRecurringEventCommand(
            Title.Trim(),
            string.IsNullOrWhiteSpace(Location) ? null : Location.Trim(),
            Visibility.Public,
            null, null,
            "Asia/Tokyo",
            AllDay,
            startDate, startTime, endTime,
            rule));
    }

    private RecurrenceRule BuildRecurrenceRule()
    {
        var endDate  = new LocalDateValue(EndDate.Year, EndDate.Month, EndDate.Day);
        var adjustment = BuildAdjustmentRule();

        return (ViewModels.RepeatTypeIndex)_repeatTypeIndex switch
        {
            ViewModels.RepeatTypeIndex.Weekly => new RecurrenceRule(
                RecurrenceType.Weekly, _interval, endDate,
                weekly: new WeeklyRule(CollectWeekdays()),
                adjustment: adjustment),

            ViewModels.RepeatTypeIndex.Monthly => new RecurrenceRule(
                RecurrenceType.Monthly, _interval, endDate,
                monthly: BuildMonthlyRule(),
                adjustment: adjustment),

            ViewModels.RepeatTypeIndex.Yearly => new RecurrenceRule(
                RecurrenceType.Yearly, _interval, endDate,
                yearly: BuildYearlyRule(),
                adjustment: adjustment),

            _ => throw new InvalidOperationException("Unexpected recurrence type.")
        };
    }

    private MonthlyRule BuildMonthlyRule() =>
        (MonthlyRuleIndex)_monthlyRuleIndex switch
        {
            MonthlyRuleIndex.DayOfMonth  => new DayOfMonthMonthlyRule(_dayOfMonth),
            MonthlyRuleIndex.NthWeekday  => new NthWeekdayMonthlyRule(
                PickerIndexToWeekIndex(_weekIndexPickerIndex),
                (Weekday)_monthlyWeekdayIndex),
            _ => throw new InvalidOperationException()
        };

    private YearlyRule BuildYearlyRule() =>
        _yearlyRuleIndex == 0
            ? new DayOfMonthYearlyRule(_yearlyMonth, _yearlyDay)
            : new NthWeekdayYearlyRule(
                _yearlyMonth,
                PickerIndexToWeekIndex(_yearlyWeekIndexPickerIndex),
                (Weekday)_yearlyWeekdayIndex);

    private AdjustmentRule? BuildAdjustmentRule()
    {
        if (_adjustmentIndex == (int)ViewModels.AdjustmentIndex.None) return null;

        BusinessCalendarId? calId = null;
        if (_selectedCalendarIndex >= 0 && _selectedCalendarIndex < _availableCalendarIds.Count)
            calId = new BusinessCalendarId(_availableCalendarIds[_selectedCalendarIndex]);

        var direction = _adjustmentIndex == (int)ViewModels.AdjustmentIndex.Forward
            ? AdjustmentDirection.Forward
            : AdjustmentDirection.Backward;

        return new AdjustmentRule(
            AdjustmentCondition.Holiday,
            AdjustmentShiftUnit.BusinessDay,
            direction == AdjustmentDirection.Forward ? 1 : -1,
            calId);
    }

    private List<Weekday> CollectWeekdays()
    {
        var days = new List<Weekday>();
        if (_weekSun) days.Add(Weekday.Sunday);
        if (_weekMon) days.Add(Weekday.Monday);
        if (_weekTue) days.Add(Weekday.Tuesday);
        if (_weekWed) days.Add(Weekday.Wednesday);
        if (_weekThu) days.Add(Weekday.Thursday);
        if (_weekFri) days.Add(Weekday.Friday);
        if (_weekSat) days.Add(Weekday.Saturday);
        return days;
    }

    private static int PickerIndexToWeekIndex(int index) =>
        index switch { 5 => -1, _ => index + 1 };  // 0→1st,…,4→5th, 5→Last(-1)

    private void LoadAvailableCalendars()
    {
        AvailableCalendarNames.Clear();
        _availableCalendarIds.Clear();
        foreach (var cal in _calendarRepo.FindAll().OrderBy(c => c.Name))
        {
            AvailableCalendarNames.Add(cal.Name);
            _availableCalendarIds.Add(cal.Id.Value);
        }
        OnPropertyChanged(nameof(HasAvailableCalendars));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
