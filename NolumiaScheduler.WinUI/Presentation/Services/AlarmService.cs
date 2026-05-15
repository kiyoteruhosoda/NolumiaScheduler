using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using NolumiaScheduler.Domain.Repositories;
using NolumiaScheduler.Domain.Services;
using NolumiaScheduler.Domain.ValueObjects;
using NolumiaScheduler.Resources.Strings;

namespace NolumiaScheduler.Presentation.Services;

public class AlarmService(ICalendarEventRepository eventRepo, IOccurrenceExpander expander) : IAlarmService
{
    private readonly ICalendarEventRepository _eventRepo = eventRepo;
    private readonly IOccurrenceExpander _expander = expander;
    private DispatcherQueueTimer? _timer;
    private readonly HashSet<string> _firedKeys = [];
    private readonly List<SnoozeEntry> _snoozed = [];
    private bool _isShowingNotification;
    private DispatcherQueue? _dispatcherQueue;

    public void Start()
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _timer = _dispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(30);
        _timer.Tick += (_, _) => _ = CheckAlarmsAsync();
        _timer.Start();
    }

    public void Stop() => _timer?.Stop();

    public void ResetFiredKeys(string eventId)
    {
        _firedKeys.RemoveWhere(k => k.StartsWith(eventId, StringComparison.Ordinal));
    }

    private async Task CheckAlarmsAsync()
    {
        if (_isShowingNotification) return;

        var now = DateTime.Now;
        var today = new LocalDateValue(now.Year, now.Month, now.Day);
        var tomorrow = today.AddDays(1);

        var dueSnoozes = _snoozed.Where(s => s.NotifyAt <= now).ToList();
        foreach (var snooze in dueSnoozes)
        {
            _snoozed.Remove(snooze);
            var snoozeMsg = string.Format(AppResources.AlarmSnoozeReminder, snooze.Title);
            await ShowAlarmAsync(snooze.EventId, snooze.Title, snoozeMsg);
            if (_isShowingNotification) return;
        }

        var events = _eventRepo.FindAll();
        foreach (var ev in events)
        {
            if (ev.Alarm == null || !ev.Alarm.IsEnabled) continue;

            var occurrences = _expander.Expand(ev, today, tomorrow, null);
            foreach (var occ in occurrences)
            {
                if (occ.AllDay || occ.StartTime == null) continue;

                var occStart = new DateTime(
                    occ.Date.Year, occ.Date.Month, occ.Date.Day,
                    occ.StartTime.Hour, occ.StartTime.Minute, occ.StartTime.Second);

                var minutesBefore = new[] { 15, 5, 1 };
                var notifyFlags = new[] { ev.Alarm.Notify15Min, ev.Alarm.Notify5Min, ev.Alarm.Notify1Min };

                for (int i = 0; i < minutesBefore.Length; i++)
                {
                    if (!notifyFlags[i]) continue;

                    var min = minutesBefore[i];
                    var key = $"{ev.Id.Value}:{occ.Date}:{min}";
                    if (_firedKeys.Contains(key)) continue;

                    var notifyAt = occStart.AddMinutes(-min);
                    if (now >= notifyAt.AddMinutes(-1) && now <= notifyAt.AddSeconds(30))
                    {
                        _firedKeys.Add(key);
                        var msg = GetMinuteMessage(min);
                        await ShowAlarmAsync(ev.Id.Value, occ.Title.Value, msg);
                        if (_isShowingNotification) return;
                    }
                }
            }
        }
    }

    private static string GetMinuteMessage(int minutes) => minutes switch
    {
        15 => AppResources.AlarmNotify15MinMsg,
        5  => AppResources.AlarmNotify5MinMsg,
        _  => AppResources.AlarmNotify1MinMsg
    };

    private async Task ShowAlarmAsync(string eventId, string title, string message)
    {
        _isShowingNotification = true;
        try
        {
            var tcs = new TaskCompletionSource<AlarmNotificationResult>();

            // Show via ContentDialog on UI thread
            var dq = _dispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
            dq.TryEnqueue(async () =>
            {
                try
                {
                    var window = NolumiaScheduler.WinUI.App.MainWindow;
                    if (window?.Content is Microsoft.UI.Xaml.FrameworkElement root)
                    {
                        var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
                        {
                            Title = title,
                            Content = message,
                            PrimaryButtonText = AppResources.AlarmDismiss,
                            SecondaryButtonText = AppResources.AlarmSnooze5Min,
                            CloseButtonText = AppResources.AlarmSnooze1Min,
                            XamlRoot = root.XamlRoot
                        };
                        var result = await dialog.ShowAsync();
                        var alarmResult = result switch
                        {
                            Microsoft.UI.Xaml.Controls.ContentDialogResult.Secondary => AlarmNotificationResult.Snooze5Min,
                            Microsoft.UI.Xaml.Controls.ContentDialogResult.None => AlarmNotificationResult.Snooze1Min,
                            _ => AlarmNotificationResult.Dismiss
                        };
                        tcs.TrySetResult(alarmResult);
                    }
                    else
                    {
                        tcs.TrySetResult(AlarmNotificationResult.Dismiss);
                    }
                }
                catch
                {
                    tcs.TrySetResult(AlarmNotificationResult.Dismiss);
                }
            });

            var notificationResult = await tcs.Task;

            if (notificationResult == AlarmNotificationResult.Snooze5Min)
                _snoozed.Add(new SnoozeEntry(eventId, title, DateTime.Now.AddMinutes(5)));
            else if (notificationResult == AlarmNotificationResult.Snooze1Min)
                _snoozed.Add(new SnoozeEntry(eventId, title, DateTime.Now.AddMinutes(1)));
        }
        finally
        {
            _isShowingNotification = false;
        }
    }

    private record SnoozeEntry(string EventId, string Title, DateTime NotifyAt);

    public IReadOnlyList<AlarmScheduleEntry> GetScheduledAlarms()
    {
        var now = DateTime.Now;
        var today = new LocalDateValue(now.Year, now.Month, now.Day);
        var tomorrow = today.AddDays(1);
        var results = new List<AlarmScheduleEntry>();

        var events = _eventRepo.FindAll();
        foreach (var ev in events)
        {
            if (ev.Alarm == null || !ev.Alarm.IsEnabled) continue;

            var occurrences = _expander.Expand(ev, today, tomorrow, null);
            foreach (var occ in occurrences)
            {
                if (occ.AllDay || occ.StartTime == null) continue;

                var occStart = new DateTime(
                    occ.Date.Year, occ.Date.Month, occ.Date.Day,
                    occ.StartTime.Hour, occ.StartTime.Minute, occ.StartTime.Second);

                var minutesBefore = new[] { 15, 5, 1 };
                var notifyFlags = new[] { ev.Alarm.Notify15Min, ev.Alarm.Notify5Min, ev.Alarm.Notify1Min };

                for (int i = 0; i < minutesBefore.Length; i++)
                {
                    if (!notifyFlags[i]) continue;
                    var min = minutesBefore[i];
                    var key = $"{ev.Id.Value}:{occ.Date}:{min}";
                    var notifyAt = occStart.AddMinutes(-min);
                    results.Add(new AlarmScheduleEntry(
                        ev.Id.Value, occ.Title.Value, occStart, min, notifyAt,
                        _firedKeys.Contains(key), false));
                }
            }
        }

        foreach (var s in _snoozed)
        {
            results.Add(new AlarmScheduleEntry(
                s.EventId, s.Title, default, 0, s.NotifyAt, false, true));
        }

        return [.. results.OrderBy(e => e.NotifyAt)];
    }

    public IReadOnlyList<string> GetFiredKeys() => [.. _firedKeys];

    public async Task ShowTestAlarmAsync()
    {
        await ShowAlarmAsync("test-debug", "テストアラーム", "これはアラームウィンドウのテスト表示です");
    }
}
