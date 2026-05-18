using NolumiaScheduler.Presentation.Services;
using NolumiaScheduler.Presentation.ViewModels;
using NolumiaScheduler.Resources.Strings;

namespace NolumiaScheduler.Presentation.Pages;

[QueryProperty(nameof(EventId), "eventId")]
[QueryProperty(nameof(StartDate), "startDate")]
[QueryProperty(nameof(StartMinute), "startMinute")]
[QueryProperty(nameof(OccurrenceDate), "occurrenceDate")]
[QueryProperty(nameof(OccurrenceStartMinute), "occurrenceStartMinute")]
public partial class EventEditPage : ContentPage
{
    private readonly EventEditViewModel _vm;
    private readonly IAlarmService _alarmService;

    private string? _eventId;
    private string? _startDateRaw;
    private string? _startMinuteRaw;
    private string? _occurrenceDateRaw;
    private string? _occurrenceStartMinuteRaw;

    public string? EventId { set { _eventId = value; ApplyNavigationContext(); } }
    public string? StartDate { set { _startDateRaw = value; ApplyNavigationContext(); } }
    public string? StartMinute { set { _startMinuteRaw = value; ApplyNavigationContext(); } }
    public string? OccurrenceDate { set { _occurrenceDateRaw = value; ApplyNavigationContext(); } }
    public string? OccurrenceStartMinute { set { _occurrenceStartMinuteRaw = value; ApplyNavigationContext(); } }

    public EventEditPage(EventEditViewModel vm, IAlarmService alarmService)
    {
        InitializeComponent();
        _vm = vm;
        _alarmService = alarmService;
        BindingContext = vm;

        vm.SaveCompleted += async () =>
        {
            if (_vm.EditingEventId is { } id)
                _alarmService.ResetFiredKeys(id);
            await Shell.Current.GoToAsync("..");
        };
    }

    private void ApplyNavigationContext()
    {
        if (!string.IsNullOrWhiteSpace(_eventId))
        {
            var occurrenceKey = BuildOccurrenceKey();
            _vm.LoadEvent(_eventId, occurrenceKey);
            return;
        }

        if (DateOnly.TryParse(_startDateRaw, out var date))
        {
            var minute = int.TryParse(_startMinuteRaw, out var parsedMinute) ? parsedMinute : 9 * 60;
            _vm.InitializeNewEvent(date, minute);
        }
    }


    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        if (!_vm.RequiresRecurringEditScopeSelection)
        {
            _vm.RequestSave();
            return;
        }

        var action = await DisplayActionSheetAsync(
            "繰り返し予定の編集",
            AppResources.CancelButton,
            null,
            "この予定のみ",
            "これ以降の予定",
            "すべての予定");

        var scope = action switch
        {
            var a when a == "この予定のみ" => RecurringEditScope.ThisOccurrence,
            var a when a == "これ以降の予定" => RecurringEditScope.ThisAndFollowing,
            var a when a == "すべての予定" => RecurringEditScope.EntireSeries,
            _ => (RecurringEditScope?)null
        };

        if (scope != null)
            _vm.Save(scope);
    }

    private Domain.ValueObjects.OccurrenceLocalKey? BuildOccurrenceKey()
    {
        if (!DateOnly.TryParse(_occurrenceDateRaw, out var date)) return null;

        var startMinute = int.TryParse(_occurrenceStartMinuteRaw, out var parsed) ? parsed : 0;
        startMinute = Math.Clamp(startMinute, 0, 1439);

        var time = new Domain.ValueObjects.LocalTimeValue(startMinute / 60, startMinute % 60, 0);
        return new Domain.ValueObjects.OccurrenceLocalKey(new Domain.ValueObjects.LocalDateValue(date.Year, date.Month, date.Day), time);
    }
}

