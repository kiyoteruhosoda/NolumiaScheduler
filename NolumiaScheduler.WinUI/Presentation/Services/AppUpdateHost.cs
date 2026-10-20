using Microsoft.UI.Dispatching;
using NolumiaScheduler.Infrastructure.Auth;
using NolumiaScheduler.Infrastructure.Diagnostics;
using Velopack;
using Velopack.Sources;

namespace NolumiaScheduler.WinUI.Presentation.Services;

/// <summary>
/// Keeps the installed app up to date (task #551, ADR-0007), the same shape as WinTrack: Velopack
/// reads the release index and packages through the Scheduler server's relay
/// (<see cref="Infrastructure.Server.SchedulerApiClient.ReleasesUri"/>, Bearer from the assay sign-in).
/// A newer version is downloaded in the background and swapped in when the app exits; the settings
/// page can also apply it right away. A build not installed by Velopack, a signed-out user or an
/// unreachable server all just mean "could not check" and stay quiet.
/// </summary>
public sealed class AppUpdateHost(Uri releasesUri, IAccessTokenSource tokens) : IDisposable
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    private DispatcherQueueTimer? _timer;
    private bool _running;
    private UpdateInfo? _ready;

    /// <summary>Raised on the UI thread when a new version has been downloaded.</summary>
    public event Action? ReadyChanged;

    /// <summary>The downloaded version waiting to be swapped in, or null.</summary>
    public string? ReadyVersion => _ready?.TargetFullRelease.Version.ToString();

    /// <summary>True when this process was installed by Velopack (false for a dev build run in place).</summary>
    public static bool IsInstalled
    {
        get
        {
            try
            {
                return new UpdateManager(new SimpleWebSource("https://localhost/")).IsInstalled;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return false;
            }
        }
    }

    public void Start()
    {
        if (!IsInstalled) return;
        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = Interval;
        _timer.Tick += (_, _) => _ = CheckAsync("timer");
        _timer.Start();
        _ = CheckAsync("startup");
    }

    private async Task CheckAsync(string reason)
    {
        if (_running) return;
        _running = true;
        try
        {
            var manager = Manager();
            var found = await manager.CheckForUpdatesAsync();
            if (found is null || found.TargetFullRelease.Version == _ready?.TargetFullRelease.Version) return;

            await manager.DownloadUpdatesAsync(found);
            // The app stays resident in the tray for days, so the swap waits for this process to
            // exit; the next start runs the new version. No restart: quitting means quitting.
            manager.WaitExitThenApplyUpdates(found.TargetFullRelease, silent: true, restart: false);
            _ready = found;
            AppLog.Current.Info(AppLogCategories.Lifecycle,
                $"Update {ReadyVersion} downloaded ({reason}); it is applied when the app exits.");
            ReadyChanged?.Invoke();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            AppLog.Current.Warning(AppLogCategories.Lifecycle, $"Could not check for updates ({reason}).", ex);
        }
        finally
        {
            _running = false;
        }
    }

    /// <summary>Swaps in the downloaded version now and starts it again.</summary>
    public void ApplyAndRestart()
    {
        if (_ready is null) return;
        Manager().ApplyUpdatesAndRestart(_ready.TargetFullRelease);
    }

    public void Dispose() => _timer?.Stop();

    private UpdateManager Manager() =>
        new(new SimpleWebSource(releasesUri, new BearerFileDownloader(tokens)));

    /// <summary>Adds the assay access token to every request Velopack makes to the relay.</summary>
    private sealed class BearerFileDownloader(IAccessTokenSource tokens) : HttpClientFileDownloader
    {
        public override async Task DownloadFile(
            string url,
            string targetFile,
            Action<int> progress,
            IDictionary<string, string>? headers,
            double timeout,
            CancellationToken cancelToken) =>
            await base.DownloadFile(url, targetFile, progress, await WithBearerAsync(headers, cancelToken), timeout, cancelToken);

        public override async Task<byte[]> DownloadBytes(string url, IDictionary<string, string>? headers, double timeout) =>
            await base.DownloadBytes(url, await WithBearerAsync(headers, CancellationToken.None), timeout);

        public override async Task<string> DownloadString(string url, IDictionary<string, string>? headers, double timeout) =>
            await base.DownloadString(url, await WithBearerAsync(headers, CancellationToken.None), timeout);

        private async Task<IDictionary<string, string>> WithBearerAsync(
            IDictionary<string, string>? headers, CancellationToken cancellationToken)
        {
            var result = headers is null ? new Dictionary<string, string>() : new Dictionary<string, string>(headers);
            if (await tokens.GetAccessTokenAsync(cancellationToken) is { } token)
            {
                result["Authorization"] = $"Bearer {token}";
            }

            return result;
        }
    }
}
