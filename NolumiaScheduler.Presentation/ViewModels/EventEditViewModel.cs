using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using NolumiaScheduler.Application;
using NolumiaScheduler.Application.Commands;
using NolumiaScheduler.Application.Services;
using NolumiaScheduler.Domain.ValueObjects;
using NolumiaScheduler.Presentation.Helpers;
using NolumiaScheduler.Presentation.Resources.Strings;

namespace NolumiaScheduler.Presentation.ViewModels;

/// <summary>Repeat type selection index: 0=None 1=Weekly 2=Monthly 3=Yearly</summary>
public enum RepeatTypeIndex { None = 0, Weekly = 1, Monthly = 2, Yearly = 3 }

/// <summary>Monthly rule index: 0=DayOfMonth 1=NthWeekday</summary>
public enum MonthlyRuleIndex { DayOfMonth = 0, NthWeekday = 1 }

/// <summary>Adjustment index: 0=None 1=Forward 2=Backward</summary>
public enum AdjustmentIndex { None = 0, Forward = 1, Backward = 2 }

public partial class EventEditViewModel : INotifyPropertyChanged
{
    private readonly CalendarEventApplicationService _eventService;
    private readonly BusinessCalendarApplicationService _calendarService;
    private readonly TimeProvider _clock;

    // ── Basic fields ────────────────────────────────────────────
    private string _title = "";
    private string _location = "";
    private bool _allDay;
    private DateTime _startDate;

    private TimeSpan _startTime = new(9, 0, 0);
    private TimeSpan _endTime = new(10, 0, 0);

    // ── Recurrence ───────────────────────────────────────────
    private int _repeatTypeIndex;
    private int _interval = 1;
    private bool _hasEndDate = false;
    private DateTime _endDate;

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
    private string? _editingEventId;
    private bool _wasRecurringAtLoad;
    // Set while loading a saved event so changing RepeatTypeIndex does not overwrite the saved
    // recurrence selectors with start-date defaults.
    private bool _suppressRecurrenceDefaults;
    // Start date of the loaded recurring series. The editable StartDate tracks the occurrence
    // being edited, so the end-date validation for an entire-series edit must compare against
    // the original series start, not the occurrence date.
    private DateTime _seriesStartDate;
    private string _timeZoneId = EventEditDefaults.DefaultTimeZone;

    // ── Color ────────────────────────────────────────────────
    private int _selectedColorIndex;   // index into ColorKeys; 0 = Default (no explicit color)

    // ── Alarm ────────────────────────────────────────────────
    private bool _alarmEnabled = true;
    private bool _alarmNotify15Min = true;
    private bool _alarmNotify5Min = true;
    private bool _alarmNotify1Min = true;
    private bool _alarmNotifyAtStart = true;

    // ── Constructor ──────────────────────────────────────────
    public EventEditViewModel(
        CalendarEventApplicationService eventService,
        BusinessCalendarApplicationService calendarService,
        TimeProvider clock)
    {
        _eventService = eventService;
        _calendarService = calendarService;
        _clock = clock;

        var today = Today();
        _startDate = today;
        _endDate = today.AddYears(1);
        _seriesStartDate = today;

        SaveCommand = new RelayCommand(RequestSave);

        // Default: Mon selected for weekly
        _weekMon = true;

        LoadAvailableCalendars();
    }

    private DateTime Today() => _clock.GetLocalNow().Date;

    public bool IsEditing => _editingEventId != null;
    public string? EditingEventId => _editingEventId;
    public OccurrenceLocalKey? EditingOccurrenceKey { get; private set; }
    public bool IsOccurrenceEditing => EditingOccurrenceKey != null;
    // Scope selection (this / following / entire) only makes sense when the
    // event was already recurring at load time. Adding recurrence to a single
    // event is a kind conversion handled separately in Save().
    public bool RequiresRecurringEditScopeSelection =>
        IsEditing && _wasRecurringAtLoad && IsRecurring && IsOccurrenceEditing;

    public void InitializeNewEvent(DateOnly date, int startMinute, int? endMinute = null)
    {
        if (IsEditing) return;

        StartDate = date.ToDateTime(TimeOnly.MinValue);

        var clamped = Math.Clamp(startMinute, 0, 1439);
        var snapped = (int)(Math.Round(clamped / 15d) * 15);
        snapped = Math.Clamp(snapped, 0, 23 * 60 + 45);

        StartTime = TimeSpan.FromMinutes(snapped);

        var endMinutes = endMinute.HasValue
            ? Math.Clamp(endMinute.Value, snapped + 15, 23 * 60 + 59)
            : Math.Min(snapped + 60, 23 * 60 + 59);
        EndTime = TimeSpan.FromMinutes(endMinutes);
    }
    public string PageTitle => IsEditing ? AppResources.EditEventTitle : AppResources.NewEventTitle;

    public void LoadEvent(string eventId, OccurrenceLocalKey? occurrenceKey = null)
    {
        var ev = _eventService.FindById(eventId);
        if (ev == null) return;

        _editingEventId = eventId;
        EditingOccurrenceKey = occurrenceKey;
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(IsOccurrenceEditing));
        OnPropertyChanged(nameof(RequiresRecurringEditScopeSelection));

        _timeZoneId = ev.TimeZoneId.Value;
        Title = ev.Title.Value;
        Location = ev.Location?.Value ?? "";

        _wasRecurringAtLoad = ev.IsRecurring();

        const int minutesPerDay = 24 * 60;
        // The form edits in the event's own timezone: convert the stored UTC instant to local
        // (docs/time-model.md §4). Cross-TZ editing in the viewer's zone is a follow-up.
        var tz = ev.TimeZoneId.ToTimeZoneInfo();

        if (ev.IsSingle() && ev.SingleSchedule != null)
        {
            var sched = ev.SingleSchedule;
            var local = TimeZoneInfo.ConvertTime(sched.StartUtc, tz);
            // All-day is derived from a midnight start spanning a full day (docs/time-model.md).
            AllDay = local.Hour == 0 && local.Minute == 0 && sched.DurationMinutes == minutesPerDay;
            StartDate = local.Date;
            StartTime = local.TimeOfDay;
            EndTime = AllDay ? StartTime : StartTime.Add(TimeSpan.FromMinutes(sched.DurationMinutes));
            RepeatTypeIndex = (int)ViewModels.RepeatTypeIndex.None;
        }
        else if (ev.IsRecurring() && ev.RecurringSchedule != null)
        {
            var sched = ev.RecurringSchedule;
            var anchorLocal = TimeZoneInfo.ConvertTime(sched.AnchorUtc, tz);
            AllDay = anchorLocal.Hour == 0 && anchorLocal.Minute == 0 && sched.DurationMinutes == minutesPerDay;

            var anchorDate = DateOnly.FromDateTime(anchorLocal.DateTime);
            var anchorTime = anchorLocal.TimeOfDay;
            var effectiveDate = occurrenceKey?.Date is { } kd
                ? new DateTime(kd.Year, kd.Month, kd.Day)
                : anchorLocal.Date;
            var effectiveStart = occurrenceKey?.Time is { } kt
                ? new TimeSpan(kt.Hour, kt.Minute, 0)
                : anchorTime;

            _seriesStartDate = new DateTime(anchorDate.Year, anchorDate.Month, anchorDate.Day);
            StartDate = effectiveDate;
            StartTime = effectiveStart;

            var durationMinutes = Math.Max(EventEditDefaults.MinEventDurationMinutes, sched.DurationMinutes);
            EndTime = AllDay ? StartTime : StartTime.Add(TimeSpan.FromMinutes(durationMinutes));

            LoadRecurrenceRule(sched.RecurrenceRule);
        }

        SelectedColorIndex = Math.Max(0, Array.IndexOf(ColorKeys, ev.ColorKey));

        AlarmEnabled      = ev.Alarm?.IsEnabled    ?? false;
        AlarmNotify15Min  = ev.Alarm?.Notify15Min  ?? true;
        AlarmNotify5Min   = ev.Alarm?.Notify5Min   ?? true;
        AlarmNotify1Min   = ev.Alarm?.Notify1Min   ?? true;
        AlarmNotifyAtStart = ev.Alarm?.NotifyAtStart ?? true;
    }

    private void LoadRecurrenceRule(RecurrenceRule rule)
    {
        // Loading assigns the saved recurrence selectors explicitly, so suppress the start-date
        // defaulting that RepeatTypeIndex would otherwise trigger.
        _suppressRecurrenceDefaults = true;
        try
        {
            LoadRecurrenceRuleCore(rule);
        }
        finally
        {
            _suppressRecurrenceDefaults = false;
        }
    }

    private void LoadRecurrenceRuleCore(RecurrenceRule rule)
    {
        // Set HasEndDate before EndDate: the HasEndDate setter bumps a stale default forward,
        // which would clobber an already-ended series' real end date if EndDate were set first.
        HasEndDate = rule.EndDate.Year < 9999;
        EndDate = new DateTime(rule.EndDate.Year, rule.EndDate.Month, rule.EndDate.Day);
        Interval = rule.Interval;

        switch (rule.RuleType)
        {
            case RecurrenceType.Weekly:
                RepeatTypeIndex = (int)ViewModels.RepeatTypeIndex.Weekly;
                if (rule.Weekly != null)
                {
                    WeekSun = rule.Weekly.Weekdays.Contains(Weekday.Sunday);
                    WeekMon = rule.Weekly.Weekdays.Contains(Weekday.Monday);
                    WeekTue = rule.Weekly.Weekdays.Contains(Weekday.Tuesday);
                    WeekWed = rule.Weekly.Weekdays.Contains(Weekday.Wednesday);
                    WeekThu = rule.Weekly.Weekdays.Contains(Weekday.Thursday);
                    WeekFri = rule.Weekly.Weekdays.Contains(Weekday.Friday);
                    WeekSat = rule.Weekly.Weekdays.Contains(Weekday.Saturday);
                }
                break;
            case RecurrenceType.Monthly:
                RepeatTypeIndex = (int)ViewModels.RepeatTypeIndex.Monthly;
                if (rule.Monthly is DayOfMonthMonthlyRule dom)
                {
                    MonthlyRuleIndex = (int)ViewModels.MonthlyRuleIndex.DayOfMonth;
                    DayOfMonth = dom.Day;
                }
                else if (rule.Monthly is NthWeekdayMonthlyRule nth)
                {
                    MonthlyRuleIndex = (int)ViewModels.MonthlyRuleIndex.NthWeekday;
                    WeekIndexPickerIndex = nth.WeekIndex == -1 ? 5 : nth.WeekIndex - 1;
                    MonthlyWeekdayIndex = (int)nth.Weekday;
                }
                break;
            case RecurrenceType.Yearly:
                RepeatTypeIndex = (int)ViewModels.RepeatTypeIndex.Yearly;
                if (rule.Yearly is DayOfMonthYearlyRule domY)
                {
                    YearlyRuleIndex = 0;
                    YearlyMonth = domY.Month;
                    YearlyDay   = domY.Day;
                }
                else if (rule.Yearly is NthWeekdayYearlyRule nthY)
                {
                    YearlyRuleIndex = 1;
                    YearlyMonth = nthY.Month;
                    YearlyWeekIndexPickerIndex = nthY.WeekIndex == -1 ? 5 : nthY.WeekIndex - 1;
                    YearlyWeekdayIndex = (int)nthY.Weekday;
                }
                break;
        }

        if (rule.Adjustment != null)
        {
            AdjustmentIndex = rule.Adjustment.ShiftAmount > 0
                ? (int)ViewModels.AdjustmentIndex.Forward
                : (int)ViewModels.AdjustmentIndex.Backward;

            if (rule.Adjustment.CalendarId != null)
            {
                var idx = _availableCalendarIds.IndexOf(rule.Adjustment.CalendarId.Value);
                SelectedCalendarIndex = idx;
            }
        }
    }

    // ── Picker item lists ─────────────────────────────────────────────

    public static List<string> RepeatTypeItems =>
    [
        AppResources.RepeatNone,
        AppResources.RepeatWeekly,
        AppResources.RepeatMonthly,
        AppResources.RepeatYearly
    ];

    public static List<string> MonthlyRuleItems =>
    [
        AppResources.MonthlyDayOfMonth,
        AppResources.MonthlyNthWeekday
    ];

    public static List<string> YearlyRuleItems => MonthlyRuleItems;

    public static List<string> WeekIndexItems =>
    [
        "1st", "2nd", "3rd", "4th", "5th", AppResources.NthWeekLast
    ];

    public static List<string> WeekdayItems =>
    [
        AppResources.DaySun, AppResources.DayMon, AppResources.DayTue,
        AppResources.DayWed, AppResources.DayThu, AppResources.DayFri, AppResources.DaySat
    ];

    public static List<string> AdjustmentItems =>
    [
        AppResources.AdjustmentNone,
        AppResources.AdjustmentForward,
        AppResources.AdjustmentBackward
    ];

    /// <summary>Selectable color keys; index-aligned with <see cref="ColorItems"/>.</summary>
    public static readonly EventColorKey[] ColorKeys =
    [
        EventColorKey.Default,
        EventColorKey.Tomato,
        EventColorKey.Tangerine,
        EventColorKey.Banana,
        EventColorKey.Basil,
        EventColorKey.Sage,
        EventColorKey.Peacock,
        EventColorKey.Blueberry,
        EventColorKey.Lavender,
        EventColorKey.Grape,
        EventColorKey.Graphite
    ];

    public static List<string> ColorItems =>
    [
        AppResources.ColorDefault,
        AppResources.ColorTomato,
        AppResources.ColorTangerine,
        AppResources.ColorBanana,
        AppResources.ColorBasil,
        AppResources.ColorSage,
        AppResources.ColorPeacock,
        AppResources.ColorBlueberry,
        AppResources.ColorLavender,
        AppResources.ColorGrape,
        AppResources.ColorGraphite
    ];

    public ObservableCollection<string> AvailableCalendarNames { get; } = [];
    private static readonly List<string> _availableCalendarIds = [];

    // ── Properties ────────────────────────────────────────────────

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

    /// <summary>
    /// Apply a user-initiated start-time edit while preserving the current
    /// duration: the end time shifts by the same amount, clamped to the day.
    /// Use this for UI edits so the displayed end time tracks the start.
    /// </summary>
    public void SetStartTimePreservingDuration(TimeSpan newStart)
    {
        var duration = _endTime - _startTime;
        if (duration <= TimeSpan.Zero)
            duration = TimeSpan.FromMinutes(EventEditDefaults.MinEventDurationMinutes);

        StartTime = newStart;

        if (AllDay) return;

        var newEnd = newStart + duration;
        if (newEnd >= TimeSpan.FromDays(1))
            newEnd = new TimeSpan(23, 59, 0);
        EndTime = newEnd;
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
            var becameRecurring = value != _repeatTypeIndex
                && value != (int)ViewModels.RepeatTypeIndex.None;
            _repeatTypeIndex = value;

            // Seed the weekday / day-of-month / month defaults from the start date when the user
            // picks a recurrence type (not while loading an existing event, which assigns the
            // saved values right after). Apply before notifying so the view reads fresh values.
            if (becameRecurring && !_suppressRecurrenceDefaults)
                ApplyStartDateRecurrenceDefaults();

            OnPropertyChanged();
            OnPropertyChanged(nameof(IsRecurring));
            OnPropertyChanged(nameof(IsWeekly));
            OnPropertyChanged(nameof(IsMonthly));
            OnPropertyChanged(nameof(IsYearly));
            OnPropertyChanged(nameof(IntervalUnitLabel));
            OnPropertyChanged(nameof(RequiresRecurringEditScopeSelection));
        }
    }

    // Default the recurrence selectors to the start date: weekly selects the start weekday,
    // monthly uses the start day-of-month (and the matching nth-weekday), yearly uses the start
    // month/day.
    private void ApplyStartDateRecurrenceDefaults()
    {
        var start = _startDate;
        var dow = start.DayOfWeek;
        var weekIndex = Math.Clamp((start.Day - 1) / 7, 0, 4); // 0=1st … 4=5th occurrence

        WeekSun = dow == DayOfWeek.Sunday;
        WeekMon = dow == DayOfWeek.Monday;
        WeekTue = dow == DayOfWeek.Tuesday;
        WeekWed = dow == DayOfWeek.Wednesday;
        WeekThu = dow == DayOfWeek.Thursday;
        WeekFri = dow == DayOfWeek.Friday;
        WeekSat = dow == DayOfWeek.Saturday;

        DayOfMonth = start.Day;
        MonthlyWeekdayIndex = (int)dow;     // 0=Sun … 6=Sat (matches WeekdayItems / Weekday enum)
        WeekIndexPickerIndex = weekIndex;

        YearlyMonth = start.Month;
        YearlyDay = start.Day;
        YearlyWeekdayIndex = (int)dow;
        YearlyWeekIndexPickerIndex = weekIndex;
    }

    public int Interval
    {
        get => _interval;
        set { _interval = Math.Max(1, value); OnPropertyChanged(); }
    }

    public bool HasEndDate
    {
        get => _hasEndDate;
        set
        {
            _hasEndDate = value;
            if (value && _endDate < Today())
                _endDate = Today().AddYears(1);
            OnPropertyChanged();
            OnPropertyChanged(nameof(EndDate));
        }
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

    // ── Color properties ────────────────────────────────────────

    public int SelectedColorIndex
    {
        get => _selectedColorIndex;
        set
        {
            _selectedColorIndex = Math.Clamp(value, 0, ColorKeys.Length - 1);
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasCustomColor));
        }
    }

    public EventColorKey SelectedColorKey => ColorKeys[_selectedColorIndex];
    public bool HasCustomColor => SelectedColorKey != EventColorKey.Default;

    // ── Alarm properties ────────────────────────────────────────

    public bool AlarmEnabled
    {
        get => _alarmEnabled;
        set
        {
            _alarmEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowAlarmNotifyOptions));
        }
    }

    public bool AlarmNotify15Min
    {
        get => _alarmNotify15Min;
        set { _alarmNotify15Min = value; OnPropertyChanged(); }
    }

    public bool AlarmNotify5Min
    {
        get => _alarmNotify5Min;
        set { _alarmNotify5Min = value; OnPropertyChanged(); }
    }

    public bool AlarmNotify1Min
    {
        get => _alarmNotify1Min;
        set { _alarmNotify1Min = value; OnPropertyChanged(); }
    }

    public bool AlarmNotifyAtStart
    {
        get => _alarmNotifyAtStart;
        set { _alarmNotifyAtStart = value; OnPropertyChanged(); }
    }

    public bool ShowAlarmNotifyOptions => _alarmEnabled;

    // ── Computed visibility ──────────────────────────────────────────

    public bool ShowTimeSection    => !AllDay;
    public bool IsRecurring        => _repeatTypeIndex != (int)ViewModels.RepeatTypeIndex.None;
    public bool IsWeekly           => _repeatTypeIndex == (int)ViewModels.RepeatTypeIndex.Weekly;
    public bool IsMonthly          => _repeatTypeIndex == (int)ViewModels.RepeatTypeIndex.Monthly;
    public bool IsYearly           => _repeatTypeIndex == (int)ViewModels.RepeatTypeIndex.Yearly;
    public bool IsMonthlyDayOfMonth  => _monthlyRuleIndex == (int)ViewModels.MonthlyRuleIndex.DayOfMonth;
    public bool IsMonthlyNthWeekday  => _monthlyRuleIndex == (int)ViewModels.MonthlyRuleIndex.NthWeekday;
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

    // ── Save logic ────────────────────────────────────────────────

    public void RequestSave() => Save(null);

    public void Save(RecurringEditScope? scope = null)
    {
        ValidationError = "";

        if (string.IsNullOrWhiteSpace(Title))
        {
            ValidationError = AppResources.ErrorTitleRequired;
            return;
        }

        try
        {
            if (_editingEventId != null)
            {
                // Changing the event kind (single <-> recurring) cannot be done
                // through UpdateEvent / OverrideOccurrence / ChangeFollowing,
                // which all assume the original kind. Replace by delete + create
                // so the saved event has the correct schedule shape.
                if (_wasRecurringAtLoad != IsRecurring)
                {
                    if (IsRecurring)
                    {
                        var selectedDays = CollectWeekdays();
                        if (IsWeekly && selectedDays.Count == 0)
                        {
                            ValidationError = AppResources.ErrorWeekdayRequired;
                            return;
                        }
                    }
                    _eventService.DeleteEvent(_editingEventId);
                    if (IsRecurring) SaveRecurring(); else SaveSingle();
                    CompleteSaveIfValid();
                    return;
                }

                if (RequiresRecurringEditScopeSelection)
                {
                    if (scope == null)
                    {
                        ValidationError = "編集範囲を選択してください。";
                        return;
                    }

                    if (scope == RecurringEditScope.ThisOccurrence)
                    {
                        SaveThisOccurrence(_editingEventId);
                        if (!string.IsNullOrEmpty(ValidationError)) return;
                        CompleteSaveIfValid();
                        return;
                    }

                    if (scope == RecurringEditScope.ThisAndFollowing)
                    {
                        SaveThisAndFollowing(_editingEventId);
                        if (!string.IsNullOrEmpty(ValidationError)) return;
                        CompleteSaveIfValid();
                        return;
                    }

                    // RecurringEditScope.EntireSeries falls through to the series update below.
                }

                // Editing a recurring event (entire series, or a recurring event opened without
                // an occurrence context) must redefine the recurrence rule and times, not just
                // the details. UpdateExisting only handles single events.
                if (_wasRecurringAtLoad && IsRecurring)
                {
                    SaveEntireSeries(_editingEventId);
                    if (!string.IsNullOrEmpty(ValidationError)) return;
                    CompleteSaveIfValid();
                    return;
                }

                UpdateExisting(_editingEventId);
            }
            else if (!IsRecurring)
                SaveSingle();
            else
                SaveRecurring();

            CompleteSaveIfValid();
        }
        catch (Exception ex)
        {
            ValidationError = ex.Message;
        }
    }


    private void CompleteSaveIfValid()
    {
        if (string.IsNullOrEmpty(ValidationError))
            SaveCompleted?.Invoke();
    }


    private void SaveThisAndFollowing(string eventId)
    {
        if (EditingOccurrenceKey == null)
        {
            ValidationError = "発生日情報がありません。";
            return;
        }

        // The new (following) series starts at the split occurrence date, so the end date must
        // be on or after that date — not the original series start.
        var followingStart = new DateTime(
            EditingOccurrenceKey.Date.Year, EditingOccurrenceKey.Date.Month, EditingOccurrenceKey.Date.Day);
        if (!EndDateIsValid(followingStart))
        {
            ValidationError = AppResources.ErrorEndDateBeforeStart;
            return;
        }

        var newStart = AllDay ? null : new LocalTimeValue(StartTime.Hours, StartTime.Minutes, 0);
        var newEnd = AllDay ? null : new LocalTimeValue(EndTime.Hours, EndTime.Minutes, 0);

        var newRule = BuildRecurrenceRule();

        _eventService.ChangeFollowingOccurrences(new ChangeFollowingOccurrencesCommand(
            eventId,
            EditingOccurrenceKey,
            Title.Trim(),
            string.IsNullOrWhiteSpace(Location) ? null : Location.Trim(),
            NolumiaScheduler.Domain.ValueObjects.Visibility.Public,
            AllDay,
            newStart,
            newEnd,
            newRule,
            Alarm: _alarmEnabled ? new EventAlarm(true, _alarmNotify15Min, _alarmNotify5Min, _alarmNotify1Min, _alarmNotifyAtStart) : null,
            ColorKey: SelectedColorKey));
    }

    private void SaveEntireSeries(string eventId)
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

        if (!EndDateIsValid(_seriesStartDate))
        {
            ValidationError = AppResources.ErrorEndDateBeforeStart;
            return;
        }

        var rule = BuildRecurrenceRule();
        var startTime = new LocalTimeValue(StartTime.Hours, StartTime.Minutes, 0);
        var endTime   = new LocalTimeValue(EndTime.Hours,   EndTime.Minutes,   0);

        _eventService.UpdateRecurringSeries(new UpdateRecurringSeriesCommand(
            eventId,
            Title.Trim(),
            string.IsNullOrWhiteSpace(Location) ? null : Location.Trim(),
            NolumiaScheduler.Domain.ValueObjects.Visibility.Public,
            startTime,
            endTime,
            rule,
            Alarm: _alarmEnabled ? new EventAlarm(true, _alarmNotify15Min, _alarmNotify5Min, _alarmNotify1Min, _alarmNotifyAtStart) : null,
            ColorKey: SelectedColorKey));
    }

    private void SaveThisOccurrence(string eventId)
    {
        if (EditingOccurrenceKey == null)
        {
            ValidationError = "発生日情報がありません。";
            return;
        }

        var date = LocalDateValue.FromDateOnly(DateOnly.FromDateTime(StartDate.Date));
        var start = AllDay ? null : new LocalTimeValue(StartTime.Hours, StartTime.Minutes, 0);
        var end = AllDay ? null : new LocalTimeValue(EndTime.Hours, EndTime.Minutes, 0);

        _eventService.OverrideOccurrence(new OverrideOccurrenceCommand(
            eventId,
            EditingOccurrenceKey,
            Title.Trim(),
            string.IsNullOrWhiteSpace(Location) ? null : Location.Trim(),
            NolumiaScheduler.Domain.ValueObjects.Visibility.Public,
            AllDay,
            date,
            start,
            end));
    }

    private void UpdateExisting(string eventId)
    {
        var ev = _eventService.FindById(eventId);
        DateOnly? newDate = null;
        TimeSpan? newStartTime = null, newEndTime = null;

        if (ev?.IsSingle() == true)
        {
            newDate      = DateOnly.FromDateTime(StartDate);
            newStartTime = AllDay ? null : StartTime;
            newEndTime   = AllDay ? null : EndTime;
        }

        _eventService.UpdateEvent(new UpdateEventCommand(
            eventId,
            Title.Trim(),
            string.IsNullOrWhiteSpace(Location) ? null : Location.Trim(),
            NolumiaScheduler.Domain.ValueObjects.Visibility.Public,
            AllDay,
            newDate,
            newStartTime,
            newEndTime,
            _alarmEnabled ? new EventAlarm(true, _alarmNotify15Min, _alarmNotify5Min, _alarmNotify1Min, _alarmNotifyAtStart) : null,
            ColorKey: SelectedColorKey));
    }

    private void SaveSingle()
    {
        _eventService.CreateSingleEvent(new CreateSingleEventCommand(
            Title.Trim(),
            string.IsNullOrWhiteSpace(Location) ? null : Location.Trim(),
            NolumiaScheduler.Domain.ValueObjects.Visibility.Public,
            null, null,
            _timeZoneId,
            AllDay,
            DateOnly.FromDateTime(StartDate),
            AllDay ? TimeSpan.Zero : StartTime,
            AllDay ? TimeSpan.Zero : EndTime,
            Alarm: _alarmEnabled ? new EventAlarm(true, _alarmNotify15Min, _alarmNotify5Min, _alarmNotify1Min, _alarmNotifyAtStart) : null,
            ColorKey: SelectedColorKey));
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

        if (!EndDateIsValid(StartDate))
        {
            ValidationError = AppResources.ErrorEndDateBeforeStart;
            return;
        }

        var rule = BuildRecurrenceRule();
        var startDate = new LocalDateValue(StartDate.Year, StartDate.Month, StartDate.Day);
        LocalTimeValue? startTime = AllDay ? null : new LocalTimeValue(StartTime.Hours, StartTime.Minutes, 0);
        LocalTimeValue? endTime   = AllDay ? null : new LocalTimeValue(EndTime.Hours,   EndTime.Minutes,   0);

        _eventService.CreateRecurringEvent(new CreateRecurringEventCommand(
            Title.Trim(),
            string.IsNullOrWhiteSpace(Location) ? null : Location.Trim(),
            NolumiaScheduler.Domain.ValueObjects.Visibility.Public,
            null, null,
            "Asia/Tokyo",
            AllDay,
            startDate, startTime, endTime,
            rule,
            Alarm: _alarmEnabled ? new EventAlarm(true, _alarmNotify15Min, _alarmNotify5Min, _alarmNotify1Min, _alarmNotifyAtStart) : null,
            ColorKey: SelectedColorKey));
    }

    private RecurrenceRule BuildRecurrenceRule()
    {
        var endDate = _hasEndDate
            ? new LocalDateValue(_endDate.Year, _endDate.Month, _endDate.Day)
            : new LocalDateValue(9999, 12, 31);
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
        (ViewModels.MonthlyRuleIndex)_monthlyRuleIndex switch
        {
            ViewModels.MonthlyRuleIndex.DayOfMonth  => new DayOfMonthMonthlyRule(_dayOfMonth),
            ViewModels.MonthlyRuleIndex.NthWeekday  => new NthWeekdayMonthlyRule(
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

    // Reject a recurrence end date that falls before the series start. Without this the domain
    // throws a raw (unlocalized) exception, or the series silently expands to zero occurrences.
    private bool EndDateIsValid(DateTime seriesStart)
        => !_hasEndDate || _endDate.Date >= seriesStart.Date;

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
        foreach (var cal in _calendarService.FindAll().OrderBy(c => c.Name))
        {
            AvailableCalendarNames.Add(cal.Name);
            _availableCalendarIds.Add(cal.Id.Value);
        }
        OnPropertyChanged(nameof(HasAvailableCalendars));
    }

    // ── Delete logic ──────────────────────────────────────────────

    public event Action? DeleteCompleted;

    public void DeleteEntireEvent()
    {
        if (_editingEventId == null) return;
        _eventService.DeleteEvent(_editingEventId);
        DeleteCompleted?.Invoke();
    }

    public void DeleteOccurrence()
    {
        if (_editingEventId == null || EditingOccurrenceKey == null) return;
        _eventService.DeleteOccurrence(new SkipOccurrenceCommand(_editingEventId, EditingOccurrenceKey));
        DeleteCompleted?.Invoke();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
