using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppNotifications;
using NolumiaScheduler.Application.Services;
using NolumiaScheduler.Domain.ValueObjects;
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
    private AlarmNotificationWindow? _currentWindow;
    private string? _currentDueEventId;

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
        var now = _clock.GetLocalNow().DateTime;

        // The app lives in the tray for weeks, so a startup-only purge is not enough.
        if (DateOnly.FromDateTime(now) != _lastPurgeDate)
            PurgeExpiredEvents();

        if (_isShowingNotification)
        {
            // A newer alarm for the same event has become due while an old one is still on screen
            // (e.g. the 15-min alarm left open until the 5-min fires). Close the stale window so the
            // newer alarm replaces it on the next tick.
            if (_currentDueEventId != null && _alarms.HasUnshownDueAlarm(_currentDueEventId))
                _currentWindow?.RequestClose();
            return;
        }

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
        _currentDueEventId = due.EventId;
        try
        {
            var now = _clock.GetLocalNow().DateTime;

            // Snapshot the event's current offset settings and the soonest upcoming alarm so the
            // window can show the per-event toggles and "next alarm in N min" (test alarms have
            // no backing event, so fall back to the defaults).
            var ev = _eventService.FindById(due.EventId);
            var alarm = ev?.Alarm ?? EventAlarm.Default;
            var nextAlarmAt = _alarms.GetNextAlarmAfter(now)?.NotifyAt;
            var hasRemainingAlarms = _alarms.HasRemainingAlarms(due.EventId);

            var tcs = new TaskCompletionSource<AlarmNotificationResult>();

            var dq = _dispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
            dq.TryEnqueue(async () =>
            {
                try
                {
                    var alarmWindow = new AlarmNotificationWindow(
                        due.Title, message, due.Location, due.OccurrenceStart,
                        nextAlarmAt, alarm.Notify5Min, alarm.Notify1Min, hasRemainingAlarms, _clock);
                    _currentWindow = alarmWindow;
                    alarmWindow.Activate();
                    // Push to the foreground after Activate(); the window's own Activated handler
                    // also does this, but call it here as a safety net for activation timing.
                    alarmWindow.ForceToForeground();

                    var result = await alarmWindow.WaitForResultAsync();
                    tcs.TrySetResult(result);
                }
                catch
                {
                    tcs.TrySetResult(AlarmNotificationResult.Dismissed);
                }
            });

            var notificationResult = await tcs.Task;

            // Persist the per-event 5/1-min toggle changes. Saving clears all fired keys (any
            // repository write does), so snapshot and restore them — otherwise the alarm just
            // shown would immediately re-fire within the catch-up grace.
            if (notificationResult.AlarmSettingsChanged && ev is not null)
            {
                var firedSnapshot = _alarms.GetFiredKeys();
                var updated = alarm with
                {
                    Notify5Min = notificationResult.Notify5Min,
                    Notify1Min = notificationResult.Notify1Min
                };
                _eventService.SetEventAlarm(due.EventId, updated);
                _alarms.RestoreFiredKeys(firedSnapshot);
            }

            switch (notificationResult.Action)
            {
                case AlarmNotificationAction.SetNextAlarmFromNow:
                    _alarms.SetNextAlarmFromNow(due, TimeSpan.FromMinutes(notificationResult.Minutes));
                    break;
                case AlarmNotificationAction.SetNextAlarmBeforeStart:
                    _alarms.SetNextAlarmBeforeStart(due, TimeSpan.FromMinutes(notificationResult.Minutes));
                    break;
                case AlarmNotificationAction.CancelAll:
                    _alarms.CancelRemainingAlarms(due.EventId);
                    break;
            }
        }
        finally
        {
            _currentWindow = null;
            _currentDueEventId = null;
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
