using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using NolumiaScheduler.Application.Commands;
using NolumiaScheduler.Application.Services;
using NolumiaScheduler.Domain.ValueObjects;
using NolumiaScheduler.Presentation.Helpers;
using NolumiaScheduler.Presentation.Resources.Strings;

namespace NolumiaScheduler.Presentation.ViewModels;

public partial class BusinessCalendarEditViewModel : INotifyPropertyChanged
{
    private readonly BusinessCalendarApplicationService _service;

    private string? _calendarId;
    private string _name = "";
    private DateTime _newHolidayDate = DateTime.Today;
    private string _newHolidayName = "";

    public string? CalendarId
    {
        get => _calendarId;
        set
        {
            _calendarId = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsEditing));
            OnPropertyChanged(nameof(PageTitle));
            LoadCalendar();
        }
    }

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    // Workday booleans (Sun=0 … Sat=6)
    public bool WorkSunday    { get; set; }
    public bool WorkMonday    { get; set; } = true;
    public bool WorkTuesday   { get; set; } = true;
    public bool WorkWednesday { get; set; } = true;
    public bool WorkThursday  { get; set; } = true;
    public bool WorkFriday    { get; set; } = true;
    public bool WorkSaturday  { get; set; }

    public ObservableCollection<HolidayDisplayItem> Holidays { get; } = [];

    public DateTime NewHolidayDate
    {
        get => _newHolidayDate;
        set { _newHolidayDate = value; OnPropertyChanged(); }
    }

    public string NewHolidayName
    {
        get => _newHolidayName;
        set { _newHolidayName = value; OnPropertyChanged(); }
    }

    public bool IsEditing => _calendarId != null;
    public string PageTitle => IsEditing ? AppResources.EditCalendarTitle : AppResources.NewCalendarTitle;

    public ICommand AddHolidayCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand DeleteCommand { get; }

    public string? ValidationError { get; private set; }

    public BusinessCalendarEditViewModel(BusinessCalendarApplicationService service)
    {
        _service = service;
        AddHolidayCommand = new RelayCommand(AddHoliday);
        SaveCommand = new RelayCommand(Save);
        DeleteCommand = new RelayCommand(Delete);
    }

    private void LoadCalendar()
    {
        if (_calendarId == null) return;

        var cal = _service.FindById(_calendarId);
        if (cal == null) return;

        Name = cal.Name;
        SetWorkdays(cal.Workdays);

        Holidays.Clear();
        foreach (var h in cal.Holidays.OrderBy(h => h.Date.ToString()))
            Holidays.Add(CreateHolidayItem(h.Date, h.Name));
    }

    private void SetWorkdays(IReadOnlyCollection<Weekday> workdays)
    {
        WorkSunday    = workdays.Contains(Weekday.Sunday);
        WorkMonday    = workdays.Contains(Weekday.Monday);
        WorkTuesday   = workdays.Contains(Weekday.Tuesday);
        WorkWednesday = workdays.Contains(Weekday.Wednesday);
        WorkThursday  = workdays.Contains(Weekday.Thursday);
        WorkFriday    = workdays.Contains(Weekday.Friday);
        WorkSaturday  = workdays.Contains(Weekday.Saturday);
        OnPropertyChanged(nameof(WorkSunday));
        OnPropertyChanged(nameof(WorkMonday));
        OnPropertyChanged(nameof(WorkTuesday));
        OnPropertyChanged(nameof(WorkWednesday));
        OnPropertyChanged(nameof(WorkThursday));
        OnPropertyChanged(nameof(WorkFriday));
        OnPropertyChanged(nameof(WorkSaturday));
    }

    private List<Weekday> CollectWorkdays()
    {
        var days = new List<Weekday>();
        if (WorkSunday)    days.Add(Weekday.Sunday);
        if (WorkMonday)    days.Add(Weekday.Monday);
        if (WorkTuesday)   days.Add(Weekday.Tuesday);
        if (WorkWednesday) days.Add(Weekday.Wednesday);
        if (WorkThursday)  days.Add(Weekday.Thursday);
        if (WorkFriday)    days.Add(Weekday.Friday);
        if (WorkSaturday)  days.Add(Weekday.Saturday);
        return days;
    }

    private void AddHoliday()
    {
        var d = NewHolidayDate;
        var date = new LocalDateValue(d.Year, d.Month, d.Day);
        var name = string.IsNullOrWhiteSpace(NewHolidayName) ? null : NewHolidayName.Trim();

        var existing = Holidays.FirstOrDefault(h => h.Date.Equals(date));
        if (existing != null)
        {
            // Ask user whether to replace
            ReplaceHolidayRequested?.Invoke(date, name, existing);
            return;
        }

        Holidays.Add(CreateHolidayItem(date, name));
        NewHolidayName = "";
    }

    public void ReplaceHoliday(LocalDateValue date, string? name)
    {
        var existing = Holidays.FirstOrDefault(h => h.Date.Equals(date));
        if (existing != null)
            Holidays.Remove(existing);

        Holidays.Add(CreateHolidayItem(date, name));
        NewHolidayName = "";
    }

    // Fires when a duplicate date is added; args: date, new name, existing item
    public event Action<LocalDateValue, string?, HolidayDisplayItem>? ReplaceHolidayRequested;

    private HolidayDisplayItem CreateHolidayItem(LocalDateValue date, string? name)
    {
        var item = new HolidayDisplayItem(date, name);
        item.RemoveRequested += () =>
        {
            Holidays.Remove(item);
        };
        return item;
    }

    private void Save()
    {
        ValidationError = null;
        if (string.IsNullOrWhiteSpace(Name))
        {
            ValidationError = AppResources.ErrorTitleRequired;
            OnPropertyChanged(nameof(ValidationError));
            return;
        }

        var workdays = CollectWorkdays();
        var timezone = "Asia/Tokyo"; // default; could be a picker in v2

        if (_calendarId == null)
        {
            var createdId = _service.Create(new CreateBusinessCalendarCommand(Name.Trim(), timezone, workdays));
            foreach (var h in Holidays)
                _service.AddHoliday(new AddHolidayCommand(createdId, h.Date, h.Name));
        }
        else
        {
            _service.Update(new UpdateBusinessCalendarCommand(_calendarId, Name.Trim(), workdays));

            var existing = _service.FindById(_calendarId)!;
            var datesToRemove = existing.Holidays.Select(h => h.Date).ToList();

            foreach (var date in datesToRemove)
                _service.RemoveHoliday(new RemoveHolidayCommand(_calendarId, date));

            foreach (var h in Holidays)
                _service.AddHoliday(new AddHolidayCommand(_calendarId, h.Date, h.Name));
        }

        SaveCompleted?.Invoke();
    }

    private void Delete()
    {
        if (_calendarId == null) return;
        _service.Delete(new DeleteBusinessCalendarCommand(_calendarId));
        DeleteCompleted?.Invoke();
    }

    public event Action? SaveCompleted;
    public event Action? DeleteCompleted;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class HolidayDisplayItem
{
    public LocalDateValue Date { get; }
    public string? Name { get; }
    public string FormattedDate { get; }
    public string DisplayText => Name == null ? FormattedDate : $"{FormattedDate}  {Name}";

    public ICommand RemoveCommand { get; }
    public event Action? RemoveRequested;

    public HolidayDisplayItem(LocalDateValue date, string? name)
    {
        Date = date;
        Name = name;
        var d = date.ToDateOnly();
        FormattedDate = d.ToString(AppResources.SelectedDayFormat, AppResources.FormatCulture);
        RemoveCommand = new RelayCommand(() => RemoveRequested?.Invoke());
    }
}
