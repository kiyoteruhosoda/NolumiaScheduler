using Microsoft.UI.Dispatching;
using NolumiaScheduler.Application.ExternalCalendars;
using NolumiaScheduler.Domain.ExternalCalendars;
using NolumiaScheduler.Infrastructure.Diagnostics;
using NolumiaScheduler.Infrastructure.ExternalCalendars;

namespace NolumiaScheduler.WinUI.Presentation.Services;

/// <summary>
/// Periodically imports the Power Automate Outlook JSON feed (settings from
/// <c>external-calendar.json</c>). The file is read on a background thread; reconciliation runs on
/// the UI thread because repository change notifications drive the UI and alarms.
/// A file watcher triggers an early sync when OneDrive delivers a new file.
/// </summary>
public sealed class ExternalCalendarSyncHost(
    ExternalCalendarConfig config,
    ExternalCalendarSyncService syncService) : IDisposable
{
    public const string OutlookSourceId = "outlook";

    private readonly ExternalCalendarConfig _config = config;
    private readonly ExternalCalendarSyncService _syncService = syncService;
    private readonly PowerAutomateJsonCalendarSource _source = new(OutlookSourceId, config.FilePath);
    private DispatcherQueue? _dispatcher;
    private DispatcherQueueTimer? _timer;
    private DispatcherQueueTimer? _debounce;
    private FileSystemWatcher? _watcher;
    private int _running;

    public void Start()
    {
        if (!_config.Enabled)
        {
            AppLog.Current.Info(AppLogCategories.ExternalCalendar, "External calendar import is disabled.");
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
            $"External calendar import started: {_config.FilePath} every {_config.IntervalMinutes} min.");
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

    private void RunSync(string reason)
    {
        if (Interlocked.Exchange(ref _running, 1) == 1) return;

        _ = Task.Run(() => _source.ReadSnapshot()).ContinueWith(task =>
        {
            var enqueued = _dispatcher?.TryEnqueue(() =>
            {
                try
                {
                    if (task.IsFaulted)
                    {
                        AppLog.Current.Error(AppLogCategories.ExternalCalendar, "Reading the external calendar failed.", task.Exception);
                        return;
                    }

                    var result = _syncService.Apply(OutlookSourceId, task.Result);
                    if (!result.SourceAvailable)
                        AppLog.Current.Warning(AppLogCategories.ExternalCalendar,
                            $"External calendar file unavailable ({reason}); keeping previous events.");
                    else if (result.Added + result.Updated + result.Removed > 0)
                        AppLog.Current.Info(AppLogCategories.ExternalCalendar,
                            $"External calendar synced ({reason}): +{result.Added} ~{result.Updated} -{result.Removed} ={result.Unchanged}.");
                }
                catch (Exception ex)
                {
                    AppLog.Current.Error(AppLogCategories.ExternalCalendar, "External calendar sync failed.", ex);
                }
                finally
                {
                    Interlocked.Exchange(ref _running, 0);
                }
            }) ?? false;
            if (!enqueued) Interlocked.Exchange(ref _running, 0);
        }, TaskScheduler.Default);
    }

    public void Dispose()
    {
        _timer?.Stop();
        _debounce?.Stop();
        _watcher?.Dispose();
        _watcher = null;
    }
}
