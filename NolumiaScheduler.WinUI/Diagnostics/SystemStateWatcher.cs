using System.Globalization;
using System.Runtime.InteropServices;
using NolumiaScheduler.Infrastructure.Diagnostics;

namespace NolumiaScheduler.WinUI.Diagnostics;

/// <summary>
/// Logs the machine-level transitions the app lives through: sleep/hibernate, resume, display
/// on/off, lid open/close, workstation lock/unlock and logoff/shutdown.
/// <para>
/// This is the piece that turns "it seems to die around suspend/resume" into evidence. On its
/// own a crash log says what broke; combined with these breadcrumbs — and with the last event
/// stamped into <see cref="AppSessionMarker"/> — it says what the machine was doing at the time,
/// including for a death that produced no exception at all.
/// </para>
/// </summary>
internal sealed class SystemStateWatcher : IDisposable
{
    private const string WindowClassName = "NolumiaSchedulerSystemStateWatcher";

    private const uint WM_QUERYENDSESSION = 0x0011;
    private const uint WM_ENDSESSION = 0x0016;
    private const uint WM_POWERBROADCAST = 0x0218;
    private const uint WM_WTSSESSION_CHANGE = 0x02B1;

    private const int PBT_APMSUSPEND = 0x0004;
    private const int PBT_APMRESUMECRITICAL = 0x0006;
    private const int PBT_APMRESUMESUSPEND = 0x0007;
    private const int PBT_APMPOWERSTATUSCHANGE = 0x000A;
    private const int PBT_APMRESUMEAUTOMATIC = 0x0012;
    private const int PBT_POWERSETTINGCHANGE = 0x8013;

    private const int WTS_CONSOLE_CONNECT = 0x1;
    private const int WTS_CONSOLE_DISCONNECT = 0x2;
    private const int WTS_REMOTE_CONNECT = 0x3;
    private const int WTS_REMOTE_DISCONNECT = 0x4;
    private const int WTS_SESSION_LOCK = 0x7;
    private const int WTS_SESSION_UNLOCK = 0x8;

    private const int NOTIFY_FOR_THIS_SESSION = 0;
    private const int DEVICE_NOTIFY_WINDOW_HANDLE = 0;

    // Offsets into POWERBROADCAST_SETTING { GUID PowerSetting; DWORD DataLength; UCHAR Data[1]; }
    private const int PowerSettingDataLengthOffset = 16;
    private const int PowerSettingDataOffset = 20;

    private static readonly Guid GuidConsoleDisplayState =
        new("6FE69556-704A-47A0-8F24-C28D936FDA47");
    private static readonly Guid GuidLidSwitchStateChange =
        new("BA3E0F4D-B817-4094-A2D1-D56379E6A0F3");

    private readonly IAppLog _log;
    private readonly TimeProvider _clock;
    private WNDPROC? _wndProc;
    private nint _hWnd;
    private nint _displayStateNotification;
    private nint _lidSwitchNotification;
    private bool _sessionNotificationRegistered;
    private DateTimeOffset? _suspendedAt;

    /// <summary>
    /// Raised with a short event name (<c>suspend</c>, <c>resume</c>, <c>display-off</c>, …) so the
    /// caller can stamp it into the session marker.
    /// </summary>
    public event Action<string>? StateChanged;

    /// <summary>
    /// Must be constructed on a thread that pumps messages (the UI thread). The window is a
    /// hidden top-level window rather than a message-only window on purpose: message-only
    /// windows are not delivered broadcast messages, and <c>WM_POWERBROADCAST</c> suspend/resume
    /// notifications are broadcasts.
    /// </summary>
    public SystemStateWatcher(IAppLog log, TimeProvider clock)
    {
        _log = log;
        _clock = clock;

        _wndProc = WndProc;
        var windowClass = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = GetModuleHandle(null),
            lpszClassName = WindowClassName,
        };
        RegisterClassEx(ref windowClass);

        _hWnd = CreateWindowEx(
            0, WindowClassName, "", 0, 0, 0, 0, 0,
            nint.Zero, nint.Zero, windowClass.hInstance, nint.Zero);

        if (_hWnd == nint.Zero)
        {
            _log.Warning(
                AppLogCategories.Power,
                $"Could not create the system-state window (error {Marshal.GetLastPInvokeError()}); " +
                "power and session transitions will not be logged.");
            return;
        }

        _sessionNotificationRegistered = WTSRegisterSessionNotification(_hWnd, NOTIFY_FOR_THIS_SESSION);

        var displayState = GuidConsoleDisplayState;
        _displayStateNotification =
            RegisterPowerSettingNotification(_hWnd, ref displayState, DEVICE_NOTIFY_WINDOW_HANDLE);

        var lidSwitch = GuidLidSwitchStateChange;
        _lidSwitchNotification =
            RegisterPowerSettingNotification(_hWnd, ref lidSwitch, DEVICE_NOTIFY_WINDOW_HANDLE);

        _log.Info(
            AppLogCategories.Power,
            $"System state watcher started (sessionNotifications={_sessionNotificationRegistered}, " +
            $"displayState={_displayStateNotification != nint.Zero}, " +
            $"lidSwitch={_lidSwitchNotification != nint.Zero}).");
    }

    private nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case WM_POWERBROADCAST:
                HandlePowerBroadcast((int)wParam, lParam);
                break;

            case WM_WTSSESSION_CHANGE:
                HandleSessionChange((int)wParam);
                break;

            case WM_QUERYENDSESSION:
                Report(AppLogLevel.Info, AppLogCategories.Session, "endsession-query",
                    "Windows is asking whether the app can close (logoff/shutdown).");
                break;

            case WM_ENDSESSION:
                if (wParam != nint.Zero)
                {
                    Report(AppLogLevel.Info, AppLogCategories.Session, "endsession",
                        $"Windows session is ending (flags=0x{(long)lParam:X}).");
                }
                break;
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void HandlePowerBroadcast(int eventType, nint data)
    {
        switch (eventType)
        {
            case PBT_APMSUSPEND:
                _suspendedAt = _clock.GetLocalNow();
                Report(AppLogLevel.Info, AppLogCategories.Power, "suspend",
                    "System is suspending (sleep/hibernate).");
                break;

            case PBT_APMRESUMEAUTOMATIC:
                ReportResume("resume-automatic", "System resumed (automatic wake).");
                break;

            case PBT_APMRESUMESUSPEND:
                ReportResume("resume", "System resumed from suspend by user action.");
                break;

            case PBT_APMRESUMECRITICAL:
                // The machine lost power without a clean suspend, so state from before the
                // suspend may be inconsistent — worth flagging louder than a normal resume.
                ReportResume("resume-critical",
                    "System resumed after a critical suspend (power was lost without a clean sleep).");
                break;

            case PBT_APMPOWERSTATUSCHANGE:
                Report(AppLogLevel.Debug, AppLogCategories.Power, "power-status",
                    "AC/battery power status changed.");
                break;

            case PBT_POWERSETTINGCHANGE:
                HandlePowerSettingChange(data);
                break;
        }
    }

    private void ReportResume(string eventName, string message)
    {
        var now = _clock.GetLocalNow();
        var suffix = _suspendedAt is { } suspendedAt
            // Wall-clock gap, which is the only way to see how long the machine was away:
            // tick counts and stopwatches do not advance across sleep.
            ? string.Format(
                CultureInfo.InvariantCulture,
                " Suspended for {0:g} (since {1:yyyy-MM-dd HH:mm:ss}).", now - suspendedAt, suspendedAt)
            : " No matching suspend was seen by this process.";

        _suspendedAt = null;
        Report(AppLogLevel.Info, AppLogCategories.Power, eventName, message + suffix);
    }

    private void HandlePowerSettingChange(nint data)
    {
        if (data == nint.Zero)
            return;

        var setting = Marshal.PtrToStructure<Guid>(data);
        var dataLength = Marshal.ReadInt32(data, PowerSettingDataLengthOffset);
        if (dataLength < 1)
            return;

        var value = Marshal.ReadByte(data, PowerSettingDataOffset);

        if (setting == GuidConsoleDisplayState)
        {
            // 0 = off, 1 = on, 2 = dimmed. Display power-down is when the compositor releases
            // graphics resources, so a crash right after "display-off" points somewhere very
            // different from one right after "resume".
            var (name, description) = value switch
            {
                0 => ("display-off", "Display turned off."),
                1 => ("display-on", "Display turned on."),
                2 => ("display-dimmed", "Display dimmed."),
                _ => ("display-unknown", $"Display state changed to {value}."),
            };
            Report(AppLogLevel.Info, AppLogCategories.Power, name, description);
        }
        else if (setting == GuidLidSwitchStateChange)
        {
            var (name, description) = value == 0
                ? ("lid-closed", "Laptop lid closed.")
                : ("lid-opened", "Laptop lid opened.");
            Report(AppLogLevel.Info, AppLogCategories.Power, name, description);
        }
    }

    private void HandleSessionChange(int changeType)
    {
        var (name, description) = changeType switch
        {
            WTS_SESSION_LOCK => ("session-lock", "Workstation locked."),
            WTS_SESSION_UNLOCK => ("session-unlock", "Workstation unlocked."),
            WTS_CONSOLE_CONNECT => ("console-connect", "Session connected to the console."),
            WTS_CONSOLE_DISCONNECT => ("console-disconnect", "Session disconnected from the console."),
            WTS_REMOTE_CONNECT => ("remote-connect", "Session connected from a remote desktop."),
            WTS_REMOTE_DISCONNECT => ("remote-disconnect", "Session disconnected from a remote desktop."),
            _ => (string.Empty, string.Empty),
        };

        if (name.Length != 0)
            Report(AppLogLevel.Info, AppLogCategories.Session, name, description);
    }

    private void Report(AppLogLevel level, string category, string eventName, string message)
    {
        _log.Write(level, category, $"{eventName}: {message}");
        StateChanged?.Invoke(eventName);
    }

    public void Dispose()
    {
        if (_displayStateNotification != nint.Zero)
        {
            UnregisterPowerSettingNotification(_displayStateNotification);
            _displayStateNotification = nint.Zero;
        }

        if (_lidSwitchNotification != nint.Zero)
        {
            UnregisterPowerSettingNotification(_lidSwitchNotification);
            _lidSwitchNotification = nint.Zero;
        }

        if (_hWnd != nint.Zero)
        {
            if (_sessionNotificationRegistered)
            {
                WTSUnRegisterSessionNotification(_hWnd);
                _sessionNotificationRegistered = false;
            }

            DestroyWindow(_hWnd);
            _hWnd = nint.Zero;
        }

        // Released only after the window is gone: the OS may still call into the thunk while
        // the window exists, and collecting the delegate before then would crash the process.
        _wndProc = null;
    }

    private delegate nint WNDPROC(nint hWnd, uint msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public nint hIconSm;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(nint hWnd);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint RegisterPowerSettingNotification(
        nint hRecipient, ref Guid powerSettingGuid, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterPowerSettingNotification(nint handle);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSRegisterSessionNotification(nint hWnd, uint dwFlags);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSUnRegisterSessionNotification(nint hWnd);
}
