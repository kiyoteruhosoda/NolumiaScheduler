using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppNotifications;
using NolumiaScheduler.Application.Services;
using NolumiaScheduler.Domain.ValueObjects;
using NolumiaScheduler.Infrastructure.Diagnostics;
using NolumiaScheduler.Presentation.Resources.Strings;
using NolumiaScheduler.Presentation.Services;
using NolumiaScheduler.WinUI.Presentation.Pages;

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
    private readonly List<(string EventId, AlarmNotificationWindow Window)> _stayWindows = [];

    public event Action? ScheduleChanged;

    public void Start()
    {
        PurgeExpiredEvents();

        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _timer = _dispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) => RunCheck();
        _timer.Start();

        _eventService.Changed += OnRepositoryChanged;

        AppLog.Current.Info(AppLogCategories.Alarm, "Alarm polling started.");
    }

    public void Stop()
    {
        _timer?.Stop();
        _eventService.Changed -= OnRepositoryChanged;

        AppLog.Current.Info(AppLogCategories.Alarm, "Alarm polling stopped.");
    }

    /// <summary>
    /// Starts an alarm check without awaiting it, logging any fault.
    /// <para>
    /// The bare fire-and-forget this replaces sent every failure into an unobserved task, so a
    /// throwing check silently killed alarms for the rest of the run while the app looked
    /// perfectly healthy.
    /// </para>
    /// </summary>
    private void RunCheck()
    {
        _ = CheckAlarmsAsync().ContinueWith(
            task => AppLog.Current.Error(
                AppLogCategories.Alarm, "Alarm check failed; polling continues.", task.Exception),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void OnRepositoryChanged()
    {
        var dq = _dispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
        dq.TryEnqueue(() =>
        {
            ScheduleChanged?.Invoke();
            RunCheck();
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
            // A window left open in "stay" mode for this event is the previous alarm of the same
            // reservation. The new alarm replaces it — in the full-screen attention presentation —
            // instead of leaving two countdowns for one event on screen.
            CloseStayWindows(due.EventId);

            var message = GetMessage(due);
            ShowAppNotification(due, message);
            await ShowAlarmAsync(due, message);
        }
    }

    /// <summary>
    /// Releases the "an alarm is on screen" slot when the user parks a window in stay mode. That
    /// window is a movable countdown the user keeps around while working, so holding the slot for
    /// it would swallow every later alarm — of any event — until it is finally closed.
    /// </summary>
    private void OnAlarmStayed(string eventId, AlarmNotificationWindow window)
    {
        _stayWindows.Add((eventId, window));

        if (!ReferenceEquals(_currentWindow, window)) return;

        _currentWindow = null;
        _currentDueEventId = null;
        _isShowingNotification = false;
    }

    private void CloseStayWindows(string eventId)
    {
        for (var i = _stayWindows.Count - 1; i >= 0; i--)
        {
            var (stayedEventId, window) = _stayWindows[i];
            if (stayedEventId != eventId) continue;

            _stayWindows.RemoveAt(i);
            window.RequestClose();
        }
    }

    private void PurgeExpiredEvents()
    {
        _lastPurgeDate = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var purged = _purgeService.PurgeExpiredEvents();
        if (purged > 0)
            AppLog.Current.Info(AppLogCategories.Alarm, $"Purged {purged} expired event(s).");
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
            AppLog.Current.Warning(AppLogCategories.Alarm, "Toast notification could not be shown.", ex);
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
            var nextAlarmAt = _alarms.GetNextAlarmTimeForOccurrence(due.EventId, due.OccurrenceStart, now);
            // "Cancel remaining alarms" is only meaningful while THIS occurrence still has a later
            // alarm. At the at-start ("Just") alarm — or after a snooze with nothing after it —
            // there is nothing left to cancel, so hide it (a recurring event's future occurrences
            // are a separate matter and must not keep the button visible here).
            var hasRemainingAlarms = nextAlarmAt.HasValue;

            var tcs = new TaskCompletionSource<AlarmNotificationResult>();
            // Tracked so the cleanup below only releases state this alarm still owns: once the user
            // parks this window in stay mode a later alarm takes over _currentWindow, and clearing
            // it unconditionally would drop that newer alarm's bookkeeping.
            AlarmNotificationWindow? shownWindow = null;

            var dq = _dispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
            dq.TryEnqueue(async () =>
            {
                try
                {
                    var alarmWindow = new AlarmNotificationWindow(
                        due.Title, message, due.Location, due.OccurrenceStart,
                        nextAlarmAt, alarm.Notify5Min, alarm.Notify1Min, hasRemainingAlarms, _clock);
                    alarmWindow.Stayed += (_, _) => OnAlarmStayed(due.EventId, alarmWindow);
                    shownWindow = alarmWindow;
                    _currentWindow = alarmWindow;
                    alarmWindow.Activate();
                    // Push to the foreground after Activate(); the window's own Activated handler
                    // also does this, but call it here as a safety net for activation timing.
                    alarmWindow.ForceToForeground();

                    var result = await alarmWindow.WaitForResultAsync();
                    tcs.TrySetResult(result);
                }
                catch (Exception ex)
                {
                    // Treating the alarm as dismissed keeps the service alive, but silently
                    // swallowing this made a broken alarm window indistinguishable from a user
                    // clicking "dismiss" — the alarm just never appeared.
                    AppLog.Current.Error(
                        AppLogCategories.Alarm,
                        $"Alarm window failed for event {due.EventId}; treating it as dismissed.", ex);
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
            if (shownWindow is not null)
                _stayWindows.RemoveAll(entry => ReferenceEquals(entry.Window, shownWindow));

            // A window that failed to open never became _currentWindow, but it did take the slot.
            if (shownWindow is null || ReferenceEquals(_currentWindow, shownWindow))
            {
                _currentWindow = null;
                _currentDueEventId = null;
                _isShowingNotification = false;
            }
        }
    }

    public async Task ShowTestAlarmAsync()
    {
        var due = new DueAlarm(
            "test-debug", AppResources.AlarmTestEventTitle, "https://example.com",
            OffsetMinutes: 0, IsSnoozeReminder: false,
            _clock.GetLocalNow().DateTime.AddMinutes(30));
        await ShowAlarmAsync(due, AppResources.AlarmTestEventMessage);
    }
}
