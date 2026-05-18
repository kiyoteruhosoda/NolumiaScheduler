using NolumiaScheduler.Domain.Repositories;
using NolumiaScheduler.Presentation.Services;

namespace NolumiaScheduler.Presentation.Pages;

public partial class AlarmDebugPage : ContentPage
{
    private readonly IAlarmService _alarmService;
    private readonly ICalendarEventRepository _eventRepo;
    private IDispatcherTimer? _refreshTimer;

    public AlarmDebugPage(IAlarmService alarmService, ICalendarEventRepository eventRepo)
    {
        InitializeComponent();
        _alarmService = alarmService;
        _eventRepo = eventRepo;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Refresh();

        _refreshTimer = Dispatcher.CreateTimer();
        _refreshTimer.Interval = TimeSpan.FromSeconds(10);
        _refreshTimer.Tick += (_, _) => Refresh();
        _refreshTimer.Start();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _refreshTimer?.Stop();
        _refreshTimer = null;
    }

    private void OnRefreshClicked(object? sender, EventArgs e) => Refresh();

    private async void OnTestAlarmClicked(object? sender, EventArgs e)
    {
        await _alarmService.ShowTestAlarmAsync();
    }

    private void Refresh()
    {
        var now = DateTime.Now;
        var allEvents = _eventRepo.FindAll();
        var alarmEnabledCount = allEvents.Count(e => e.Alarm is { IsEnabled: true });
        var firedKeys = _alarmService.GetFiredKeys();

        DiagLabel.Text = $"現在時刻: {now:HH:mm:ss}  |  イベント総数: {allEvents.Count}  |  アラーム有効: {alarmEnabledCount}  |  発火済キー: {firedKeys.Count}";

        var entries = _alarmService.GetScheduledAlarms();
        AlarmList.ItemsSource = entries.Select(e => new AlarmDebugItem(e, now)).ToList();
    }
}

internal class AlarmDebugItem
{
    public string Title { get; }
    public string StatusBadge { get; }
    public Color StatusColor { get; }
    public string Detail { get; }
    public string NotifyAtText { get; }

    public AlarmDebugItem(AlarmScheduleEntry entry, DateTime now)
    {
        Title = entry.Title;

        if (entry.IsSnoozed)
        {
            StatusBadge = "  [スヌーズ]";
            StatusColor = Colors.Orange;
        }
        else if (entry.AlreadyFired)
        {
            StatusBadge = "  [発火済]";
            StatusColor = Colors.Gray;
        }
        else
        {
            var remaining = entry.NotifyAt - now;
            if (remaining.TotalMinutes <= 0)
            {
                StatusBadge = "  [発火待ち]";
                StatusColor = Colors.Red;
            }
            else
            {
                StatusBadge = $"  [あと{remaining.TotalMinutes:F0}分]";
                StatusColor = Colors.Green;
            }
        }

        Detail = entry.IsSnoozed
            ? "スヌーズ再通知"
            : $"開始: {entry.OccurrenceStart:HH:mm}  |  {entry.MinutesBefore}分前通知";

        NotifyAtText = $"通知時刻: {entry.NotifyAt:yyyy/MM/dd HH:mm:ss}  |  EventId: {entry.EventId[..Math.Min(8, entry.EventId.Length)]}...";
    }
}
