using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using NolumiaScheduler.Infrastructure.Diagnostics;

namespace NolumiaScheduler.WinUI.Diagnostics;

/// <summary>
/// Samples process health on a background timer and keeps the session marker's liveness
/// timestamp fresh.
/// <para>
/// Two failure modes are invisible without this. First, a slow resource leak: this app is meant
/// to sit in the tray for weeks, and a GDI/USER handle leak (each window, icon and menu costs
/// handles) hits the per-process 10,000 limit and kills the process with no managed exception —
/// the "it was fine yesterday" crash. Second, a UI-thread hang: the window stops responding but
/// the process is alive, which users report as a crash. Sampling from a background thread
/// catches both, and the heartbeat bounds when a silent death happened even when nothing was
/// logged at all.
/// </para>
/// </summary>
internal sealed class AppHealthMonitor : IDisposable
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromMinutes(1);

    /// <summary>Write a routine sample every N intervals; anomalies are logged immediately.</summary>
    private const int SamplesPerRoutineLogEntry = 10;

    /// <summary>Windows caps GDI and USER objects at 10,000 per process; warn before that.</summary>
    private const uint GuiResourceWarningThreshold = 8_000;

    private const long WorkingSetWarningBytes = 1_024L * 1024 * 1024;

    private static readonly TimeSpan UiStallWarningThreshold = TimeSpan.FromSeconds(30);

    private const uint GR_GDIOBJECTS = 0;
    private const uint GR_USEROBJECTS = 1;

    private readonly IAppLog _log;
    private readonly AppSessionMarker _session;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly Timer _timer;

    private long _lastUiPongTicks = Environment.TickCount64;
    private int _uiPingPending;
    private int _sampleCount;
    private bool _uiStallReported;

    /// <param name="dispatcherQueue">The UI dispatcher, pinged to detect a hung UI thread.</param>
    public AppHealthMonitor(IAppLog log, AppSessionMarker session, DispatcherQueue dispatcherQueue)
    {
        _log = log;
        _session = session;
        _dispatcherQueue = dispatcherQueue;
        _timer = new Timer(_ => Sample(reason: null), null, SampleInterval, SampleInterval);
    }

    /// <summary>
    /// Forces a sample outside the timer, tagged with why. Used around suspend/resume so the log
    /// shows the process state on both sides of the gap.
    /// </summary>
    public void SampleNow(string reason) => Sample(reason);

    private void Sample(string? reason)
    {
        try
        {
            _session.Heartbeat();

            var uiLag = PingUiThread();
            var (gdiObjects, userObjects) = ReadGuiResources();

            using var process = Process.GetCurrentProcess();
            var workingSet = process.WorkingSet64;

            var uiStalled = uiLag > UiStallWarningThreshold;
            var resourcesHigh =
                gdiObjects >= GuiResourceWarningThreshold
                || userObjects >= GuiResourceWarningThreshold
                || workingSet >= WorkingSetWarningBytes;

            var message = string.Format(
                CultureInfo.InvariantCulture,
                "{0}workingSet={1}MB gcHeap={2}MB handles={3} gdi={4} user={5} threads={6} uiLag={7:0.0}s",
                reason is null ? string.Empty : $"({reason}) ",
                workingSet / (1024 * 1024),
                GC.GetTotalMemory(forceFullCollection: false) / (1024 * 1024),
                process.HandleCount,
                gdiObjects,
                userObjects,
                process.Threads.Count,
                uiLag.TotalSeconds);

            // Report a stall once per stall, not once per sample, so a hung UI does not bury the
            // log — but always report the recovery so the outage has a visible end.
            if (uiStalled && !_uiStallReported)
            {
                _uiStallReported = true;
                _log.Warning(AppLogCategories.Health, "UI thread is not responding. " + message);
                return;
            }

            if (!uiStalled && _uiStallReported)
            {
                _uiStallReported = false;
                _log.Warning(AppLogCategories.Health, "UI thread responded again. " + message);
                return;
            }

            if (resourcesHigh)
            {
                _log.Warning(AppLogCategories.Health, "Resource usage is high. " + message);
                return;
            }

            if (reason is not null || ++_sampleCount % SamplesPerRoutineLogEntry == 0)
                _log.Info(AppLogCategories.Health, message);
        }
        catch (Exception ex)
        {
            // A timer callback that throws would tear down the process — the exact failure this
            // class exists to diagnose.
            _log.Error(AppLogCategories.Health, "Health sampling failed.", ex);
        }
    }

    /// <summary>
    /// Returns how long the UI thread has been unresponsive. A ping is only enqueued when the
    /// previous one has come back, so while the UI is wedged the measured lag keeps growing
    /// instead of resetting.
    /// </summary>
    private TimeSpan PingUiThread()
    {
        // Environment.TickCount64 does not advance while the machine sleeps, so a suspend does
        // not masquerade as a UI stall.
        var lag = TimeSpan.FromMilliseconds(Environment.TickCount64 - Volatile.Read(ref _lastUiPongTicks));

        if (Interlocked.CompareExchange(ref _uiPingPending, 1, 0) == 0)
        {
            var enqueued = _dispatcherQueue.TryEnqueue(() =>
            {
                Volatile.Write(ref _lastUiPongTicks, Environment.TickCount64);
                Volatile.Write(ref _uiPingPending, 0);
            });

            if (!enqueued)
                Volatile.Write(ref _uiPingPending, 0);
        }

        return lag;
    }

    private static (uint Gdi, uint User) ReadGuiResources()
    {
        var self = GetCurrentProcess();
        return (GetGuiResources(self, GR_GDIOBJECTS), GetGuiResources(self, GR_USEROBJECTS));
    }

    public void Dispose() => _timer.Dispose();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetGuiResources(nint hProcess, uint uiFlags);

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();
}
