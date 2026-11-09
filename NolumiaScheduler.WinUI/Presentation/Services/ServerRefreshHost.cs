using Microsoft.UI.Dispatching;
using NolumiaScheduler.Infrastructure.Diagnostics;
using NolumiaScheduler.Infrastructure.Server;

namespace NolumiaScheduler.WinUI.Presentation.Services;

/// <summary>
/// Reads the server copy again at startup and every <see cref="Interval"/> (server ADR-0054 decision 7;
/// writes refresh their own event). Runs on the UI thread so the store's change notification reaches
/// the calendar and the alarms on the thread they expect. A failed read only means the copy stays as it is.
/// Each round is noted in <see cref="SyncActivity"/> for the sync status window.
/// </summary>
public sealed class ServerRefreshHost(ServerCalendarStore store, SyncActivity activity) : IDisposable
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    private readonly ServerCalendarStore _store = store;
    private readonly SyncActivity _activity = activity;
    private DispatcherQueueTimer? _timer;
    private bool _running;

    public void Start()
    {
        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = Interval;
        _timer.Tick += (_, _) => _ = RefreshAsync("timer");
        _timer.Start();
        _ = RefreshAsync("startup");
    }

    /// <summary>Reads now (after an Outlook send, or the sync status window's button). Call on the UI thread.</summary>
    public void RefreshNow(string reason) => _ = RefreshAsync(reason);

    private async Task RefreshAsync(string reason)
    {
        if (_running) return;
        _running = true;
        try
        {
            var failuresBefore = _activity.ServerReadFailures;
            var failingSince = _activity.ServerReadFailingSince;
            await _store.RefreshAsync(CancellationToken.None);
            _activity.ServerRead(reason, true);
            if (failuresBefore > 0)
            {
                AppLog.Current.Info(AppLogCategories.Server,
                    $"Read the Scheduler server again ({reason}) after {failuresBefore} failed read(s) in a row since {Utc(failingSince)}.");
            }
        }
        catch (ServerException ex)
        {
            Failed(reason, ex, error: false);
        }
        catch (Exception ex)
        {
            Failed(reason, ex, error: true);
        }
        finally
        {
            _running = false;
        }
    }

    // Says which call failed and what came back (SyncActivity.Describe), how long the copy has not been
    // refreshed and how many rounds in a row have failed, so one log line explains a stale calendar.
    private void Failed(string reason, Exception ex, bool error)
    {
        var description = SyncActivity.Describe(ex);
        _activity.ServerRead(reason, false, description);
        var message =
            $"Could not read the Scheduler server ({reason}); showing the local copy. {description}. " +
            $"Failed {_activity.ServerReadFailures} time(s) in a row since {Utc(_activity.ServerReadFailingSince)}; " +
            $"last successful read {Utc(_store.FetchedAt)}.";
        if (error) AppLog.Current.Error(AppLogCategories.Server, message, ex);
        else AppLog.Current.Warning(AppLogCategories.Server, message, ex);
    }

    private static string Utc(DateTimeOffset? moment)
        => moment is { } m ? m.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", System.Globalization.CultureInfo.InvariantCulture) : "never";

    public void Dispose() => _timer?.Stop();
}
