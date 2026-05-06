using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using NolumiaScheduler.Domain.Repositories;
using NolumiaScheduler.Domain.Services;
using NolumiaScheduler.Domain.ValueObjects;
using NolumiaScheduler.Resources.Strings;

namespace NolumiaScheduler.Presentation.ViewModels;

public sealed class CalendarViewModel : INotifyPropertyChanged
{
    private readonly ICalendarEventRepository _events;
    private readonly IOccurrenceExpander _expander;

    private DateTime _month;
    private CalendarDayCell? _selectedCell;
    private string _monthYearTitle = "";
    private string _selectedDayLabel = "";
    private bool _hasSelectedDay;

    public CalendarViewModel(ICalendarEventRepository events, IOccurrenceExpander expander)
    {
        _events = events;
        _expander = expander;
        _month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

        DayCells = [];
        SelectedDayEvents = [];

        PreviousMonthCommand = new Command(() => Navigate(-1));
        NextMonthCommand = new Command(() => Navigate(1));
        GoTodayCommand = new Command(GoToday);

        LoadMonth();
    }

    public ObservableCollection<CalendarDayCell> DayCells { get; }
    public ObservableCollection<CalendarEventItem> SelectedDayEvents { get; }

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

    public ICommand PreviousMonthCommand { get; }
    public ICommand NextMonthCommand { get; }
    public ICommand GoTodayCommand { get; }

    public void SelectDay(CalendarDayCell cell)
    {
        if (_selectedCell != null)
            _selectedCell.IsSelected = false;

        _selectedCell = cell;
        cell.IsSelected = true;

        var date = cell.Date.ToDateOnly();
        SelectedDayLabel = date.ToString(AppResources.SelectedDayFormat, AppResources.FormatCulture);
        HasSelectedDay = true;

        SelectedDayEvents.Clear();
        foreach (var occ in cell.Events)
            SelectedDayEvents.Add(new CalendarEventItem(occ));
    }

    private void Navigate(int months)
    {
        ClearSelection();
        _month = _month.AddMonths(months);
        LoadMonth();
    }

    private void GoToday()
    {
        ClearSelection();
        _month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        LoadMonth();
    }

    private void ClearSelection()
    {
        if (_selectedCell != null)
            _selectedCell.IsSelected = false;
        _selectedCell = null;
        HasSelectedDay = false;
        SelectedDayEvents.Clear();
    }

    private void LoadMonth()
    {
        MonthYearTitle = _month.ToString(AppResources.MonthYearFormat, AppResources.FormatCulture);

        var today = LocalDateValue.FromDateOnly(DateOnly.FromDateTime(DateTime.Today));
        var monthFrom = LocalDateValue.FromDateOnly(DateOnly.FromDateTime(_month));
        var monthTo = LocalDateValue.FromDateOnly(
            DateOnly.FromDateTime(_month.AddMonths(1).AddDays(-1)));

        var byDate = new Dictionary<string, List<EventOccurrence>>();

        foreach (var ev in _events.FindAll())
        {
            foreach (var occ in _expander.Expand(ev, monthFrom, monthTo, null))
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

        DayCells.Clear();

        // Grid starts from the Sunday of the week containing the 1st of the month
        var gridStart = _month.AddDays(-(int)_month.DayOfWeek);

        for (var i = 0; i < 42; i++)
        {
            var date = gridStart.AddDays(i);
            var dateVal = LocalDateValue.FromDateOnly(DateOnly.FromDateTime(date));
            byDate.TryGetValue(dateVal.ToString(), out var evts);

            DayCells.Add(new CalendarDayCell
            {
                Date = dateVal,
                IsToday = dateVal.Equals(today),
                IsCurrentMonth = date.Month == _month.Month,
                Events = (IReadOnlyList<EventOccurrence>?)evts ?? [],
            });
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
