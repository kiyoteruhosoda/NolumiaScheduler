using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppNotifications;
using NolumiaScheduler.Application.Services;
using NolumiaScheduler.Presentation.Resources.Strings;
using NolumiaScheduler.Presentation.Services;
using NolumiaScheduler.WinUI.Presentation.Pages;
using System.Diagnostics;

namespace NolumiaScheduler.WinUI.Presentation.Services;

/// <summary>
/// Hosts the alarm polling timer and presents notification windows. All scheduling decisions
/// (which alarms are due, fired keys, snoozes) live in <see cref="AlarmApplicationService"/>;
/// expired events are purged at startup and on every local-date rollover.
/// </summary>
public class AlarmService(
    AlarmApplicationService alarms,
    CalendarEventApplicationService eventService,
    PurgeExpiredEventsService purgeService,
    TimeProvider clock) : IAlarmService
{
    private readonly AlarmApplicationService _alarms = alarms;
    private readonly CalendarEventApplicationService _eventService = eventService;
    private readonly PurgeExpiredEventsService _purgeService = purgeService;
    private readonly TimeProvider _clock = clock;
    private DispatcherQueueTimer? _timer;
    private bool _isShowingNotification;
    private DispatcherQueue? _dispatcherQueue;
    private DateOnly _lastPurgeDate;

    public event Action? ScheduleChanged;

    public void Start()
    {
        PurgeExpiredEvents();

        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _timer = _dispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
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
            ScheduleChanged?.Invoke();
            _ = CheckAlarmsAsync();
        });
    }

    public void ResetFiredKeys(string eventId) => _alarms.ResetFiredKeys(eventId);
    public IReadOnlyList<AlarmScheduleEntry> GetScheduledAlarms() => _alarms.GetScheduledAlarms();
    public IReadOnlyList<string> GetFiredKeys() => _alarms.GetFiredKeys();
    public IReadOnlyList<string> GetDiagnosticLines() => _alarms.GetDiagnosticLines();

    private async Task CheckAlarmsAsync()
    {
        if (_isShowingNotification) return;

        var now = _clock.GetLocalNow().DateTime;
        Debug.WriteLine($"[AlarmService] CheckAlarms at {now:HH:mm:ss}");

        // The app lives in the tray for weeks, so a startup-only purge is not enough.
        if (DateOnly.FromDateTime(now) != _lastPurgeDate)
            PurgeExpiredEvents();

        foreach (var due in _alarms.CollectDueAlarms())
        {
            var message = GetMessage(due);
            ShowAppNotification(due, message);
            await ShowAlarmAsync(due, message);
        }
    }

    private void PurgeExpiredEvents()
    {
        _lastPurgeDate = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var purged = _purgeService.PurgeExpiredEvents();
        if (purged > 0)
            Debug.WriteLine($"[AlarmService] Purged {purged} expired event(s)");
    }

    private static void ShowAppNotification(DueAlarm due, string message)
    {
        try
        {
            var title = XmlEscape(due.Title);
            var body = XmlEscape(message);
            var locationPart = due.Location is { Length: > 0 }
                ? $"<text>{XmlEscape(due.Location)}</text>"
                : "";
            var xml = $"<toast><visual><binding template=\"ToastGeneric\">" +
                      $"<text>{title}</text><text>{body}</text>{locationPart}" +
                      $"</binding></visual>" +
                      // Notification.Alarm is not a valid ms-winsoundevent value (only the
                      // Looping.Alarm* variants exist); an invalid src makes Windows reject
                      // the whole toast, so use the documented Reminder sound.
                      $"<audio src=\"ms-winsoundevent:Notification.Reminder\"/></toast>";
            AppNotificationManager.Default.Show(new AppNotification(xml));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AlarmService] App notification failed: {ex.Message}");
        }
    }

    private static string XmlEscape(string value)
        => value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                .Replace("\"", "&quot;").Replace("'", "&apos;");

    private static string GetMessage(DueAlarm due) => due.IsSnoozeReminder
        ? string.Format(AppResources.AlarmSnoozeReminder, due.Title)
        : due.OffsetMinutes switch
        {
            15 => AppResources.AlarmNotify15MinMsg,
            5  => AppResources.AlarmNotify5MinMsg,
            1  => AppResources.AlarmNotify1MinMsg,
            _  => AppResources.AlarmNotify0MinMsg
        };

    private async Task ShowAlarmAsync(DueAlarm due, string message)
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
                    var alarmWindow = new AlarmNotificationWindow(
                        due.Title, message, due.Location, due.OccurrenceStart, _clock);
                    alarmWindow.Activate();
                    // Push to the foreground after Activate(); the window's own Activated handler
                    // also does this, but call it here as a safety net for activation timing.
                    alarmWindow.ForceToForeground();

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
                    _alarms.SnoozeFor(due, TimeSpan.FromMinutes(5));
                    break;
                case AlarmNotificationResult.Snooze1Min:
                    _alarms.SnoozeFor(due, TimeSpan.FromMinutes(1));
                    break;
                case AlarmNotificationResult.CancelAll:
                    _alarms.CancelRemainingAlarms(due.EventId);
                    break;
                case AlarmNotificationResult.SnoozeTo5MinBefore:
                    _alarms.SnoozeUntilBeforeStart(due, TimeSpan.FromMinutes(5));
                    break;
                case AlarmNotificationResult.SnoozeTo1MinBefore:
                    _alarms.SnoozeUntilBeforeStart(due, TimeSpan.FromMinutes(1));
                    break;
            }
        }
        finally
        {
            _isShowingNotification = false;
        }
    }

    public async Task ShowTestAlarmAsync()
    {
        var due = new DueAlarm(
            "test-debug", "テストアラーム", "https://example.com",
            OffsetMinutes: 0, IsSnoozeReminder: false,
            _clock.GetLocalNow().DateTime.AddMinutes(30));
        await ShowAlarmAsync(due, "これはアラームウィンドウのテスト表示です");
    }
}
