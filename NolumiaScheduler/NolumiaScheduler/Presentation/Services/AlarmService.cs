using Microsoft.Maui.Dispatching;
using NolumiaScheduler.Domain.Repositories;
using NolumiaScheduler.Domain.Services;
using NolumiaScheduler.Domain.ValueObjects;
using NolumiaScheduler.Resources.Strings;
using MauiApp = Microsoft.Maui.Controls.Application;

namespace NolumiaScheduler.Presentation.Services;

public class AlarmService(ICalendarEventRepository eventRepo, IOccurrenceExpander expander) : IAlarmService
{
    private readonly ICalendarEventRepository _eventRepo = eventRepo;
    private readonly IOccurrenceExpander _expander = expander;
    private IDispatcherTimer? _timer;
    private readonly HashSet<string> _firedKeys = [];
    private readonly List<SnoozeEntry> _snoozed = [];
    private bool _isShowingNotification;

    public void Start()
    {
        _timer = MauiApp.Current!.Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(30);
        _timer.Tick += (_, _) => _ = CheckAlarmsAsync();
        _timer.Start();
    }

    public void Stop() => _timer?.Stop();

    private async Task CheckAlarmsAsync()
    {
        if (_isShowingNotification) return;

        var now = DateTime.Now;
        var today = new LocalDateValue(now.Year, now.Month, now.Day);
        var tomorrow = today.AddDays(1);

        // Fire due snoozed alarms first
        var dueSnoozes = _snoozed.Where(s => s.NotifyAt <= now).ToList();
        foreach (var snooze in dueSnoozes)
        {
            _snoozed.Remove(snooze);
            var snoozeMsg = string.Format(AppResources.AlarmSnoozeReminder, snooze.Title);
            await ShowAlarmAsync(snooze.EventId, snooze.Title, snoozeMsg);
            if (_isShowingNotification) return;
        }

        // Check regular event alarms
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
                    // Fire window: [notifyAt - 1min, notifyAt + 30sec]
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
            var app = MauiApp.Current;
            var mainPage = app is { Windows.Count: > 0 } ? app.Windows[0].Page : null;
            if (mainPage == null) return;

            var action = await mainPage.DisplayActionSheetAsync(
                $"{title}\n{message}",
                AppResources.AlarmDismiss,
                null,
                AppResources.AlarmSnooze5Min,
                AppResources.AlarmSnooze1Min);

            if (action == AppResources.AlarmSnooze5Min)
                _snoozed.Add(new SnoozeEntry(eventId, title, DateTime.Now.AddMinutes(5)));
            else if (action == AppResources.AlarmSnooze1Min)
                _snoozed.Add(new SnoozeEntry(eventId, title, DateTime.Now.AddMinutes(1)));
        }
        finally
        {
            _isShowingNotification = false;
        }
    }

    private record SnoozeEntry(string EventId, string Title, DateTime NotifyAt);
}
