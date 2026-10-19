using Microsoft.UI.Dispatching;
using NolumiaScheduler.Infrastructure.Diagnostics;
using NolumiaScheduler.Infrastructure.Server;

namespace NolumiaScheduler.WinUI.Presentation.Services;

/// <summary>
/// Reads the server copy again at startup and every <see cref="Interval"/> (server ADR-0054 decision 7;
/// writes refresh their own event). Runs on the UI thread so the store's change notification reaches
/// the calendar and the alarms on the thread they expect. A failed read only means the copy stays as it is.
/// </summary>
public sealed class ServerRefreshHost(ServerCalendarStore store) : IDisposable
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    private readonly ServerCalendarStore _store = store;
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

    private async Task RefreshAsync(string reason)
    {
        if (_running) return;
        _running = true;
        try
        {
            await _store.RefreshAsync(CancellationToken.None);
        }
        catch (ServerException ex)
        {
            AppLog.Current.Warning(AppLogCategories.Lifecycle, $"Could not read the Scheduler server ({reason}); showing the local copy.", ex);
        }
        catch (Exception ex)
        {
            AppLog.Current.Error(AppLogCategories.Lifecycle, $"Reading the Scheduler server failed ({reason}).", ex);
        }
        finally
        {
            _running = false;
        }
    }

    public void Dispose() => _timer?.Stop();
}
