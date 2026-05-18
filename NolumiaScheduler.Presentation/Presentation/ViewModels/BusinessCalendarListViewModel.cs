using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using NolumiaScheduler.Domain.Repositories;
using NolumiaScheduler.Resources.Strings;

namespace NolumiaScheduler.Presentation.ViewModels;

public sealed class BusinessCalendarListViewModel : INotifyPropertyChanged
{
    private readonly IBusinessCalendarRepository _repo;

    public ObservableCollection<BusinessCalendarSummary> Calendars { get; } = [];

    public bool HasNoCalendars => Calendars.Count == 0;

    public ICommand RefreshCommand { get; }

    public BusinessCalendarListViewModel(IBusinessCalendarRepository repo)
    {
        _repo = repo;
        RefreshCommand = new Command(Reload);
        Reload();
    }

    public void Reload()
    {
        Calendars.Clear();
        foreach (var cal in _repo.FindAll().OrderBy(c => c.Name))
        {
            Calendars.Add(new BusinessCalendarSummary(
                cal.Id.Value,
                cal.Name,
                BuildWorkdaysSummary(cal.Workdays)));
        }
        OnPropertyChanged(nameof(HasNoCalendars));
    }

    private static string BuildWorkdaysSummary(IReadOnlyCollection<Domain.ValueObjects.Weekday> workdays)
    {
        if (workdays.Count == 0) return "—";
        var abbrs = new[]
        {
            AppResources.DaySun, AppResources.DayMon, AppResources.DayTue,
            AppResources.DayWed, AppResources.DayThu, AppResources.DayFri, AppResources.DaySat
        };
        var parts = Enumerable.Range(0, 7)
            .Where(i => workdays.Contains((Domain.ValueObjects.Weekday)i))
            .Select(i => abbrs[i]);
        return string.Join(" ", parts);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record BusinessCalendarSummary(string Id, string Name, string WorkdaysSummary);
