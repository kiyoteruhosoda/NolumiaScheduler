using Microsoft.UI.Dispatching;
using NolumiaScheduler.Infrastructure.Diagnostics;
using NolumiaScheduler.Infrastructure.ExternalCalendars;
using NolumiaScheduler.Infrastructure.Server;

namespace NolumiaScheduler.WinUI.Presentation.Services;

/// <summary>
/// Periodically sends the Power Automate Outlook JSON feed to the Scheduler server (settings from
/// <c>external-calendar.json</c>; server ADR-0054 decision 6). All decisions live in
/// <see cref="OutlookFeedSender"/>; this host only owns the timers: at startup, every
/// <see cref="ExternalCalendarConfig.IntervalMinutes"/> and 5 seconds after the file changes.
/// The events come back through the server copy like any other imported calendar: after a send the
/// copy is read again at once, so the calendar does not wait for the next 5-minute read. Each round
/// is noted in <see cref="SyncActivity"/> for the sync status window.
/// </summary>
public sealed class ExternalCalendarSyncHost(
    ExternalCalendarConfig config,
    OutlookFeedSender sender,
    SyncActivity activity,
    ServerRefreshHost? refresh = null) : IDisposable
{
    private readonly ExternalCalendarConfig _config = config;
    private readonly OutlookFeedSender _sender = sender;
    private readonly SyncActivity _activity = activity;
    private readonly ServerRefreshHost? _refresh = refresh;
    private DispatcherQueue? _dispatcher;
    private DispatcherQueueTimer? _timer;
    private DispatcherQueueTimer? _debounce;
    private FileSystemWatcher? _watcher;
    private int _running;

    public void Start()
    {
        if (!_config.Enabled)
        {
            AppLog.Current.Info(AppLogCategories.ExternalCalendar, "Sending the Outlook feed is disabled.");
            return;
        }

        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _timer = _dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMinutes(_config.IntervalMinutes);
        _timer.Tick += (_, _) => RunSync("timer");
        _timer.Start();

        _debounce = _dispatcher.CreateTimer();
        _debounce.Interval = TimeSpan.FromSeconds(5);
        _debounce.IsRepeating = false;
        _debounce.Tick += (_, _) => RunSync("file changed");

        TryStartWatcher();
        AppLog.Current.Info(AppLogCategories.ExternalCalendar,
            $"Sending the Outlook feed: {_config.FilePath} every {_config.IntervalMinutes} min.");
        RunSync("startup");
    }

    private void TryStartWatcher()
    {
        try
        {
            var dir = Path.GetDirectoryName(_config.FilePath);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

            _watcher = new FileSystemWatcher(dir, Path.GetFileName(_config.FilePath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size
            };
            FileSystemEventHandler onChange = (_, _) => _dispatcher?.TryEnqueue(() =>
            {
                _debounce?.Stop();
                _debounce?.Start();
            });
            _watcher.Changed += onChange;
            _watcher.Created += onChange;
            _watcher.Renamed += (_, e) => onChange(_watcher, e);
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            AppLog.Current.Warning(AppLogCategories.ExternalCalendar, "Could not watch the external calendar file; timer sync only.", ex);
        }
    }

    /// <summary>Sends now (the sync status window's button). Does nothing while sending is disabled.</summary>
    public void SyncNow()
    {
        if (_dispatcher is null) return;
        RunSync("manual");
    }

    private void RunSync(string reason)
    {
        if (Interlocked.Exchange(ref _running, 1) == 1) return;

        _ = Task.Run(async () =>
        {
            try
            {
                var outcome = await _sender.SendAsync(CancellationToken.None);
                var sent = outcome == OutlookSendOutcome.Sent;
                _activity.OutlookSend(reason, sent, outcome.ToString());
                if (sent)
                {
                    AppLog.Current.Info(AppLogCategories.ExternalCalendar, $"Outlook feed sent to the server ({reason}).");
                    if (_refresh is { } refresh) _dispatcher?.TryEnqueue(() => refresh.RefreshNow("outlook sent"));
                }
                else
                {
                    AppLog.Current.Warning(AppLogCategories.ExternalCalendar, $"Outlook feed not sent ({reason}): {outcome}.");
                }
            }
            catch (ServerException ex)
            {
                // Not signed in, offline or refused: the next round sends the whole file again.
                _activity.OutlookSend(reason, false, "Failed", SyncActivity.Describe(ex));
                AppLog.Current.Warning(AppLogCategories.ExternalCalendar, $"Outlook feed not sent ({reason}).", ex);
            }
            catch (Exception ex)
            {
                _activity.OutlookSend(reason, false, "Failed", SyncActivity.Describe(ex));
                AppLog.Current.Error(AppLogCategories.ExternalCalendar, "Sending the Outlook feed failed.", ex);
            }
            finally
            {
                Interlocked.Exchange(ref _running, 0);
            }
        });
    }

    public void Dispose()
    {
        _timer?.Stop();
        _debounce?.Stop();
        _watcher?.Dispose();
        _watcher = null;
    }
}
