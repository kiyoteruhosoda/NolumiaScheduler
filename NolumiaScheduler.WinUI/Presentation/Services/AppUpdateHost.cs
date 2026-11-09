using Microsoft.UI.Dispatching;
using NolumiaScheduler.Infrastructure.Auth;
using NolumiaScheduler.Infrastructure.Diagnostics;
using Velopack;
using Velopack.Sources;

namespace NolumiaScheduler.WinUI.Presentation.Services;

/// <summary>What a check that the user asked for found (an automatic check stays quiet).</summary>
public enum UpdateCheckOutcome
{
    /// <summary>Not installed by Velopack (a dev build run in place): nothing to update.</summary>
    NotInstalled,
    /// <summary>Nobody has signed in for updates.</summary>
    SignedOut,
    UpToDate,
    /// <summary>A newer version is downloaded and waits for the app to quit (<see cref="AppUpdateHost.ReadyVersion"/>).</summary>
    Ready,
    Failed,
}

/// <summary>
/// Keeps the installed app up to date the way RustEdit does (task #937, ADR-0009): Velopack reads the
/// release index and packages from blobshare's relay with the update sign-in's Bearer token
/// (<see cref="Infrastructure.Updates.UpdateConnection"/>). Checks run a minute after start, every six
/// hours, right after signing in, and on request. A newer version downloads on its own, as in Chrome,
/// and waits; quitting the app installs it, so the next start is the new version (if the app dies
/// instead, Velopack installs it on the next start). The settings page can also apply it right away.
/// Automatic checks keep quiet when they cannot check; a requested one always answers.
/// </summary>
public sealed class AppUpdateHost(Uri releasesUri, SignInSession signIn) : IDisposable
{
    public static readonly TimeSpan FirstCheckAfter = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    private readonly SemaphoreSlim _checking = new(1, 1);
    private DispatcherQueue? _dispatcher;
    private DispatcherQueueTimer? _timer;
    private UpdateInfo? _ready;

    /// <summary>Raised on the UI thread when a new version has been downloaded.</summary>
    public event Action? ReadyChanged;

    /// <summary>The downloaded version waiting to be swapped in, or null.</summary>
    public string? ReadyVersion => _ready?.TargetFullRelease.Version.ToString();

    /// <summary>The sign-in that unlocks the relay (shown and driven by the settings page).</summary>
    public SignInSession SignIn => signIn;

    /// <summary>The last error of a check, for the settings page.</summary>
    public string? LastError { get; private set; }

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
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _timer = _dispatcher.CreateTimer();
        _timer.Interval = FirstCheckAfter;
        _timer.Tick += (_, _) =>
        {
            _timer.Interval = Interval;
            _ = CheckAsync("timer");
        };
        _timer.Start();
        signIn.Changed += OnSignInChanged;
    }

    /// <summary>A check the user asked for. Always answers.</summary>
    public Task<UpdateCheckOutcome> CheckNowAsync() => CheckAsync("asked");

    private void OnSignInChanged(object? sender, EventArgs e)
    {
        if (signIn.IsSignedIn)
        {
            _dispatcher?.TryEnqueue(() => _ = CheckAsync("signed in"));
        }
    }

    private async Task<UpdateCheckOutcome> CheckAsync(string reason)
    {
        if (!IsInstalled) return UpdateCheckOutcome.NotInstalled;
        if (!signIn.IsSignedIn) return UpdateCheckOutcome.SignedOut;

        await _checking.WaitAsync();
        try
        {
            var manager = Manager();
            var found = await manager.CheckForUpdatesAsync();
            if (found is null)
            {
                LastError = null;
                return _ready is null ? UpdateCheckOutcome.UpToDate : UpdateCheckOutcome.Ready;
            }

            if (found.TargetFullRelease.Version != _ready?.TargetFullRelease.Version)
            {
                await manager.DownloadUpdatesAsync(found);
                _ready = found;
                AppLog.Current.Info(AppLogCategories.Lifecycle,
                    $"Update {ReadyVersion} downloaded ({reason}); it is installed when the app quits.");
                ReadyChanged?.Invoke();
            }

            LastError = null;
            return UpdateCheckOutcome.Ready;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A refused token (expired sign-in) has already signed out in SignInSession.
            LastError = ex.Message;
            AppLog.Current.Warning(AppLogCategories.Lifecycle, $"Could not check for updates ({reason}).", ex);
            return signIn.IsSignedIn ? UpdateCheckOutcome.Failed : UpdateCheckOutcome.SignedOut;
        }
        finally
        {
            _checking.Release();
        }
    }

    /// <summary>
    /// Called while the app quits: hands the downloaded version to Velopack, which waits for this
    /// process to end and swaps it in without starting it (quitting means quitting).
    /// </summary>
    public void ApplyOnExit()
    {
        if (_ready is null) return;
        try
        {
            Manager().WaitExitThenApplyUpdates(_ready.TargetFullRelease, silent: true, restart: false);
            AppLog.Current.Info(AppLogCategories.Lifecycle, $"Update {ReadyVersion} is installed after exit.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Velopack also installs a downloaded version on the next start.
            AppLog.Current.Warning(AppLogCategories.Lifecycle, "Could not hand the update over on exit.", ex);
        }
    }

    /// <summary>Swaps in the downloaded version now and starts it again.</summary>
    public void ApplyAndRestart()
    {
        if (_ready is null) return;
        Manager().ApplyUpdatesAndRestart(_ready.TargetFullRelease);
    }

    public void Dispose()
    {
        _timer?.Stop();
        signIn.Changed -= OnSignInChanged;
    }

    private UpdateManager Manager() =>
        new(new SimpleWebSource(releasesUri, new BearerFileDownloader(signIn)));

    /// <summary>Adds the update sign-in's access token to every request Velopack makes to the relay.</summary>
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
