using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using NolumiaScheduler.Application.Services;
using NolumiaScheduler.Domain.Services;
using NolumiaScheduler.Domain.ValueObjects;
using NolumiaScheduler.Presentation.Resources.Strings;
using NolumiaScheduler.Presentation.Services;
using NolumiaScheduler.WinUI.Presentation.Pages;
using System.Diagnostics;

namespace NolumiaScheduler.WinUI.Presentation.Services;

public class AlarmService(CalendarEventApplicationService eventService, IOccurrenceExpander expander) : IAlarmService
{
    private readonly CalendarEventApplicationService _eventService = eventService;
    private readonly IOccurrenceExpander _expander = expander;
    private DispatcherQueueTimer? _timer;
    private readonly HashSet<string> _firedKeys = [];
    private readonly List<SnoozeEntry> _snoozed = [];
    private bool _isShowingNotification;
    private DispatcherQueue? _dispatcherQueue;

    public event Action? ScheduleChanged;

    public void Start()
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _timer = _dispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(10);
        _timer.Tick += (_, _) => _ = CheckAlarmsAsync();
        _timer.Start();

        _eventService.Changed += OnRepositoryChanged;
    }

    public void Stop()
    {
        _timer?.Stop();
        _eventService.Changed -= OnRepositoryChanged;
    }

    private void OnRepositoryChanged()
    {
        var dq = _dispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
        dq.TryEnqueue(() =>
        {
            _firedKeys.Clear();
            ScheduleChanged?.Invoke();
            _ = CheckAlarmsAsync();
        });
    }

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

        Debug.WriteLine($"[AlarmService] CheckAlarms at {now:HH:mm:ss}");

        var dueSnoozes = _snoozed.Where(s => s.NotifyAt <= now).ToList();
        foreach (var snooze in dueSnoozes)
        {
            _snoozed.Remove(snooze);
            var snoozeMsg = string.Format(AppResources.AlarmSnoozeReminder, snooze.Title);
            await ShowAlarmAsync(snooze.EventId, snooze.Title, snoozeMsg, snooze.Location);
            if (_isShowingNotification) return;
        }

        var events = _eventService.FindAll();
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

                var minutesBefore = new[] { 15, 5, 1, 0 };
                var notifyFlags = new[] { ev.Alarm.Notify15Min, ev.Alarm.Notify5Min, ev.Alarm.Notify1Min,
                    !ev.Alarm.Notify15Min && !ev.Alarm.Notify5Min && !ev.Alarm.Notify1Min };

                for (int i = 0; i < minutesBefore.Length; i++)
                {
                    if (!notifyFlags[i]) continue;

                    var min = minutesBefore[i];
                    var key = $"{ev.Id.Value}:{occ.Date}:{min}";
                    if (_firedKeys.Contains(key)) continue;

                    var notifyAt = occStart.AddMinutes(-min);
                    // Fire window: [notifyAt - 2min, notifyAt + 1min]
                    if (now >= notifyAt.AddMinutes(-2) && now <= notifyAt.AddMinutes(1))
                    {
                        _firedKeys.Add(key);
                        var msg = GetMinuteMessage(min);
                        await ShowAlarmAsync(ev.Id.Value, occ.Title.Value, msg, ev.Location?.Value, occStart);
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
        1  => AppResources.AlarmNotify1MinMsg,
        _  => AppResources.AlarmNotify0MinMsg
    };

    private async Task ShowAlarmAsync(string eventId, string title, string message, string? location = null, DateTime? eventStartTime = null)
    {
        _isShowingNotification = true;
        try
        {
            var tcs = new TaskCompletionSource<AlarmNotificationResult>();

            var dq = _dispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
            dq.TryEnqueue(async () =>
            {
                try
                {
                    var alarmWindow = new AlarmNotificationWindow(title, message, location, eventStartTime);
                    alarmWindow.Activate();

                    var result = await alarmWindow.WaitForResultAsync();
                    tcs.TrySetResult(result);
                }
                catch
                {
                    tcs.TrySetResult(AlarmNotificationResult.Dismiss);
                }
            });

            var notificationResult = await tcs.Task;

            switch (notificationResult)
            {
                case AlarmNotificationResult.Snooze5Min:
                    _snoozed.Add(new SnoozeEntry(eventId, title, DateTime.Now.AddMinutes(5), location));
                    break;
                case AlarmNotificationResult.Snooze1Min:
                    _snoozed.Add(new SnoozeEntry(eventId, title, DateTime.Now.AddMinutes(1), location));
                    break;
                case AlarmNotificationResult.CancelAll:
                    CancelRemainingAlarms(eventId);
                    break;
                case AlarmNotificationResult.SnoozeTo5MinBefore:
                    if (eventStartTime.HasValue)
                        _snoozed.Add(new SnoozeEntry(eventId, title, eventStartTime.Value.AddMinutes(-5), location));
                    break;
                case AlarmNotificationResult.SnoozeTo1MinBefore:
                    if (eventStartTime.HasValue)
                        _snoozed.Add(new SnoozeEntry(eventId, title, eventStartTime.Value.AddMinutes(-1), location));
                    break;
            }
        }
        finally
        {
            _isShowingNotification = false;
        }
    }

    private void CancelRemainingAlarms(string eventId)
    {
        var now = DateTime.Now;
        var today = new LocalDateValue(now.Year, now.Month, now.Day);
        var tomorrow = today.AddDays(1);

        var events = _eventService.FindAll();
        foreach (var ev in events)
        {
            if (ev.Id.Value != eventId) continue;
            if (ev.Alarm == null || !ev.Alarm.IsEnabled) continue;

            var occurrences = _expander.Expand(ev, today, tomorrow, null);
            foreach (var occ in occurrences)
            {
                if (occ.AllDay || occ.StartTime == null) continue;

                foreach (var min in new[] { 15, 5, 1, 0 })
                {
                    var key = $"{ev.Id.Value}:{occ.Date}:{min}";
                    _firedKeys.Add(key);
                }
            }
        }

        _snoozed.RemoveAll(s => s.EventId == eventId);
    }

    private record SnoozeEntry(string EventId, string Title, DateTime NotifyAt, string? Location);

    public IReadOnlyList<AlarmScheduleEntry> GetScheduledAlarms()
    {
        var now = DateTime.Now;
        var today = new LocalDateValue(now.Year, now.Month, now.Day);
        var tomorrow = today.AddDays(1);
        var results = new List<AlarmScheduleEntry>();

        var events = _eventService.FindAll();
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

                var minutesBefore = new[] { 15, 5, 1, 0 };
                var notifyFlags = new[] { ev.Alarm.Notify15Min, ev.Alarm.Notify5Min, ev.Alarm.Notify1Min,
                    !ev.Alarm.Notify15Min && !ev.Alarm.Notify5Min && !ev.Alarm.Notify1Min };

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

    public IReadOnlyList<string> GetDiagnosticLines()
    {
        var lines = new List<string>();
        var now = DateTime.Now;
        var today = new LocalDateValue(now.Year, now.Month, now.Day);
        var tomorrow = today.AddDays(1);

        var events = _eventService.FindAll();
        lines.Add($"[Diag] Total events: {events.Count}, Range: {today} ~ {tomorrow}");

        foreach (var ev in events)
        {
            var evLabel = $"{ev.Title.Value} (id={ev.Id.Value[..Math.Min(8, ev.Id.Value.Length)]})";

            if (ev.Alarm == null)
            {
                lines.Add($"  SKIP {evLabel}: Alarm is null");
                continue;
            }
            if (!ev.Alarm.IsEnabled)
            {
                lines.Add($"  SKIP {evLabel}: Alarm disabled");
                continue;
            }

            var occurrences = _expander.Expand(ev, today, tomorrow, null);
            lines.Add($"  {evLabel}: Alarm ON, Occurrences in range = {occurrences.Count}");

            foreach (var occ in occurrences)
            {
                if (occ.AllDay)
                {
                    lines.Add($"    SKIP occ {occ.Date}: AllDay=true");
                    continue;
                }
                if (occ.StartTime == null)
                {
                    lines.Add($"    SKIP occ {occ.Date}: StartTime=null");
                    continue;
                }

                var occStart = new DateTime(
                    occ.Date.Year, occ.Date.Month, occ.Date.Day,
                    occ.StartTime.Hour, occ.StartTime.Minute, occ.StartTime.Second);

                lines.Add($"    occ {occ.Date} start={occStart:HH:mm} | 15m={ev.Alarm.Notify15Min} 5m={ev.Alarm.Notify5Min} 1m={ev.Alarm.Notify1Min}");

                var minutesBefore = new[] { 15, 5, 1, 0 };
                var notifyFlags = new[] { ev.Alarm.Notify15Min, ev.Alarm.Notify5Min, ev.Alarm.Notify1Min,
                    !ev.Alarm.Notify15Min && !ev.Alarm.Notify5Min && !ev.Alarm.Notify1Min };
                for (int i = 0; i < minutesBefore.Length; i++)
                {
                    if (!notifyFlags[i]) continue;
                    var min = minutesBefore[i];
                    var notifyAt = occStart.AddMinutes(-min);
                    var key = $"{ev.Id.Value}:{occ.Date}:{min}";
                    var fired = _firedKeys.Contains(key);
                    var inWindow = now >= notifyAt.AddMinutes(-2) && now <= notifyAt.AddMinutes(1);
                    lines.Add($"      {min}min: notifyAt={notifyAt:HH:mm:ss} fired={fired} inWindow={inWindow}");
                }
            }
        }

        return lines;
    }

    public async Task ShowTestAlarmAsync()
    {
        await ShowAlarmAsync("test-debug", "テストアラーム", "これはアラームウィンドウのテスト表示です", "https://example.com", DateTime.Now.AddMinutes(30));
    }
}
