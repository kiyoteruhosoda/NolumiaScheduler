using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using NolumiaScheduler.Presentation.Resources.Strings;
using NolumiaScheduler.WinUI.Presentation.Services;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Graphics;
using WinRT;
using WinRT.Interop;

namespace NolumiaScheduler.WinUI.Presentation.Pages;

public sealed partial class AlarmNotificationWindow : Window
{
    private readonly TaskCompletionSource<AlarmNotificationResult> _tcs = new();
    private readonly string? _location;
    private bool _foregroundForced;
    private bool _overlayConfigured;

    public AlarmNotificationWindow(string title, string message, string? location, DateTime? eventStartTime, TimeProvider clock)
    {
        InitializeComponent();

        var now = clock.GetLocalNow().DateTime;

        // Set all text from resources
        Title = AppResources.AlarmWindowTitle;
        TitleLabel.Text = title;
        MessageLabel.Text = message;
        TimeLabel.Text = now.ToString("yyyy/MM/dd HH:mm");
        DismissBtn.Content = AppResources.AlarmDismiss;
        Snooze5Btn.Content = AppResources.AlarmSnooze5MinBtn;
        Snooze1Btn.Content = AppResources.AlarmSnooze1MinBtn;
        OpenLocationBtn.Content = AppResources.AlarmOpenLocation;
        Before5Btn.Content = AppResources.AlarmSnoozeTo5MinBefore;
        Before1Btn.Content = AppResources.AlarmSnoozeTo1MinBefore;
        CancelAllBtn.Content = AppResources.AlarmCancelAll;

        _location = location;

        // Show location button if location is provided
        if (!string.IsNullOrWhiteSpace(location))
        {
            OpenLocationBtn.Visibility = Visibility.Visible;
        }

        // Show before-event snooze buttons if event start time is provided and in the future
        if (eventStartTime.HasValue && eventStartTime.Value > now)
        {
            BeforeEventGrid.Visibility = Visibility.Visible;
        }

        AppWindow?.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico"));

        // Disable other app windows to simulate system-modal
        DisableOtherWindows(true);

        // The translucent overlay setup and the foreground push must run after Activate() —
        // doing them in the constructor, before the hosting service calls Activate(), is too
        // early: the HWND styles get overridden. Re-assert top-most whenever the window loses
        // focus so it stays visible (mirrors WPF's maintained Topmost).
        Activated += OnActivated;

        Closed += (_, _) =>
        {
            DisableOtherWindows(false);
            _tcs.TrySetResult(AlarmNotificationResult.Dismiss);
        };
    }

    public Task<AlarmNotificationResult> WaitForResultAsync() => _tcs.Task;

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (!_overlayConfigured)
        {
            ConfigureOverlayWindow();
            _overlayConfigured = true;
        }

        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            // Keep the alarm visually above other windows when it loses focus, without
            // aggressively stealing focus back on every deactivation.
            ReassertTopmost();
        }
        else if (!_foregroundForced)
        {
            ForceToForeground();
        }
    }

    /// <summary>
    /// Turns the window into a full-screen translucent overlay on the monitor the user is
    /// currently working on: borderless, layered (WS_EX_LAYERED), top-most, with the XAML
    /// background showing the desktop through the scrim. Clicks are NOT passed through —
    /// the alarm stays interactive.
    /// </summary>
    private void ConfigureOverlayWindow()
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            var bounds = GetActiveMonitorBounds(hwnd);

            // 1) Borderless via OverlappedPresenter (the FullScreen presenter paints an opaque
            //    backdrop of its own, which defeats per-pixel transparency).
            if (AppWindow is not null)
            {
                if (AppWindow.Presenter is OverlappedPresenter presenter)
                {
                    presenter.SetBorderAndTitleBar(false, false);
                    presenter.IsResizable = false;
                    presenter.IsMaximizable = false;
                    presenter.IsMinimizable = false;
                }

                AppWindow.MoveAndResize(bounds);
            }

            // 2) Strip the residual frame styles SetBorderAndTitleBar leaves behind, and
            //    suppress the 1px outline + rounded corners DWM still draws around
            //    borderless windows on Win11, so the overlay reaches the screen edges
            //    with no visible border.
            var style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_STYLE);
            style &= ~(NativeMethods.WS_CAPTION | NativeMethods.WS_THICKFRAME);
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_STYLE, style);

            var borderColor = unchecked((int)NativeMethods.DWMWA_COLOR_NONE);
            _ = NativeMethods.DwmSetWindowAttribute(
                hwnd,
                NativeMethods.DWMWINDOWATTRIBUTE.DWMWA_BORDER_COLOR,
                ref borderColor,
                Marshal.SizeOf<int>());

            var cornerPref = (int)NativeMethods.DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_DONOTROUND;
            _ = NativeMethods.DwmSetWindowAttribute(
                hwnd,
                NativeMethods.DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE,
                ref cornerPref,
                Marshal.SizeOf<int>());

            // 3) Layered window (no click-through: WS_EX_TRANSPARENT is intentionally absent).
            var exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            exStyle |= NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TOOLWINDOW;
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, exStyle);

            // 4) Window-wide alpha stays 255; the 50% dim comes from the semi-transparent
            //    GCalAlarmScrim brush on the root Grid, so the card itself stays fully opaque.
            NativeMethods.SetLayeredWindowAttributes(hwnd, 0, 255, NativeMethods.LWA_ALPHA);

            // 5) Keep the overlay top-most at the monitor bounds. SWP_FRAMECHANGED makes
            //    the style changes from step 2/3 take effect.
            NativeMethods.SetWindowPos(
                hwnd,
                NativeMethods.HWND_TOPMOST,
                bounds.X,
                bounds.Y,
                bounds.Width,
                bounds.Height,
                NativeMethods.SWP_SHOWWINDOW | NativeMethods.SWP_FRAMECHANGED);

            // 6) Drop the opaque WinUI backdrop so the desktop shows through the scrim.
            TryMakeWinUiBackgroundTransparent(hwnd);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AlarmNotificationWindow] ConfigureOverlayWindow failed: {ex.Message}");
        }
    }

    private void TryMakeWinUiBackgroundTransparent(nint hwnd)
    {
        try
        {
            // Swap the system backdrop for a fully transparent brush.
            var brushHolder = this.As<Windows.UI.Composition.ICompositionSupportsSystemBackdrop>();
            var compositor = new Windows.UI.Composition.Compositor();
            var colorBrush = compositor.CreateColorBrush(Windows.UI.Color.FromArgb(0, 255, 255, 255));
            brushHolder.SystemBackdrop = colorBrush;

            // Enable DWM blur-behind with an empty region — required for the transparent
            // backdrop to actually composite over the desktop instead of black.
            var rgn = NativeMethods.CreateRectRgn(-2, -2, -1, -1);
            try
            {
                var bb = new NativeMethods.DWM_BLURBEHIND
                {
                    dwFlags = NativeMethods.DWM_BB_ENABLE | NativeMethods.DWM_BB_BLURREGION,
                    fEnable = 1,
                    hRgnBlur = rgn,
                    fTransitionOnMaximized = 0
                };

                _ = NativeMethods.DwmEnableBlurBehindWindow(hwnd, ref bb);
            }
            finally
            {
                if (rgn != 0)
                {
                    NativeMethods.DeleteObject(rgn);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AlarmNotificationWindow] TryMakeWinUiBackgroundTransparent failed: {ex.Message}");
        }
    }

    private static RectInt32 GetActiveMonitorBounds(nint fallbackHwnd)
    {
        try
        {
            // Prefer the monitor that holds the current foreground window so the alarm
            // appears where the user is working, not necessarily on the primary display.
            var foreground = NativeMethods.GetForegroundWindow();
            var source = foreground != nint.Zero ? foreground : fallbackHwnd;

            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(source);
            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);

            return displayArea.OuterBounds;
        }
        catch
        {
            return new RectInt32(
                0,
                0,
                NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN),
                NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN));
        }
    }

    /// <summary>
    /// Forces the alarm window to the foreground. Windows silently ignores SetForegroundWindow
    /// from a background/tray process (foreground lock), which is exactly the case when an alarm
    /// fires while another app is active, so this attaches to the active window's input queue
    /// (AttachThreadInput) to be allowed to take focus, and flashes the taskbar if focus is still
    /// denied.
    /// </summary>
    public void ForceToForeground()
    {
        _foregroundForced = true;

        var hwnd = WindowNative.GetWindowHandle(this);

        // Restore in case the window is minimized.
        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);

        var foreground = NativeMethods.GetForegroundWindow();
        var foregroundThread = NativeMethods.GetWindowThreadProcessId(foreground, out _);
        var currentThread = NativeMethods.GetCurrentThreadId();

        var attached = false;
        if (foregroundThread != 0 && foregroundThread != currentThread)
            attached = NativeMethods.AttachThreadInput(currentThread, foregroundThread, true);

        try
        {
            NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);
            NativeMethods.BringWindowToTop(hwnd);
            var gotForeground = NativeMethods.SetForegroundWindow(hwnd);
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);

            if (!gotForeground)
                FlashTaskbar(hwnd);
        }
        finally
        {
            if (attached)
                NativeMethods.AttachThreadInput(currentThread, foregroundThread, false);
        }
    }

    private void ReassertTopmost()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);
    }

    private static void FlashTaskbar(nint hwnd)
    {
        var info = new NativeMethods.FLASHWINFO
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.FLASHWINFO>(),
            hwnd = hwnd,
            dwFlags = NativeMethods.FLASHW_ALL | NativeMethods.FLASHW_TIMERNOFG,
            uCount = uint.MaxValue,
            dwTimeout = 0
        };
        NativeMethods.FlashWindowEx(in info);
    }

    private void OnDismissClicked(object sender, RoutedEventArgs e) => Complete(AlarmNotificationResult.Dismiss);
    private void OnSnooze5Clicked(object sender, RoutedEventArgs e) => Complete(AlarmNotificationResult.Snooze5Min);
    private void OnSnooze1Clicked(object sender, RoutedEventArgs e) => Complete(AlarmNotificationResult.Snooze1Min);
    private void OnCancelAllClicked(object sender, RoutedEventArgs e) => Complete(AlarmNotificationResult.CancelAll);
    private void OnSnoozeTo5MinBeforeClicked(object sender, RoutedEventArgs e) => Complete(AlarmNotificationResult.SnoozeTo5MinBefore);
    private void OnSnoozeTo1MinBeforeClicked(object sender, RoutedEventArgs e) => Complete(AlarmNotificationResult.SnoozeTo1MinBefore);

    private void OnOpenLocationClicked(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_location)) return;

        try
        {
            Process.Start(new ProcessStartInfo(_location) { UseShellExecute = true });
        }
        catch
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", _location));
            }
            catch
            {
                // Silently ignore if location cannot be opened
            }
        }
    }

    private void Complete(AlarmNotificationResult result)
    {
        DisableOtherWindows(false);
        _tcs.TrySetResult(result);
        Close();
    }

    private static void DisableOtherWindows(bool disable)
    {
        var mainWindow = NolumiaScheduler.WinUI.App.MainWindow;
        if (mainWindow is not null)
        {
            var hwnd = WindowNative.GetWindowHandle(mainWindow);
            NativeMethods.EnableWindow(hwnd, !disable);
        }
    }

    private static partial class NativeMethods
    {
        public const int GWL_STYLE   = -16;
        public const int GWL_EXSTYLE = -20;

        public const int WS_CAPTION    = 0x00C00000;
        public const int WS_THICKFRAME = 0x00040000;

        public const int WS_EX_LAYERED    = 0x00080000;
        public const int WS_EX_TOOLWINDOW = 0x00000080;

        public const uint LWA_ALPHA = 0x00000002;

        public static readonly nint HWND_TOPMOST = -1;
        public const uint SWP_NOMOVE       = 0x0002;
        public const uint SWP_NOSIZE       = 0x0001;
        public const uint SWP_FRAMECHANGED = 0x0020;
        public const uint SWP_SHOWWINDOW   = 0x0040;
        public const int  SW_SHOW         = 5;
        public const int  SW_RESTORE      = 9;
        public const uint FLASHW_ALL       = 0x00000003;
        public const uint FLASHW_TIMERNOFG = 0x0000000C;

        public const int SM_CXSCREEN = 0;
        public const int SM_CYSCREEN = 1;

        public const uint DWM_BB_ENABLE     = 0x00000001;
        public const uint DWM_BB_BLURREGION = 0x00000002;

        public enum DWM_WINDOW_CORNER_PREFERENCE
        {
            DWMWCP_DEFAULT = 0,
            DWMWCP_DONOTROUND = 1,
            DWMWCP_ROUND = 2,
            DWMWCP_ROUNDSMALL = 3
        }

        public enum DWMWINDOWATTRIBUTE
        {
            DWMWA_WINDOW_CORNER_PREFERENCE = 33,
            DWMWA_BORDER_COLOR = 34
        }

        // Special DWMWA_BORDER_COLOR value: suppress the window border entirely.
        public const uint DWMWA_COLOR_NONE = 0xFFFFFFFE;

        [StructLayout(LayoutKind.Sequential)]
        public struct FLASHWINFO
        {
            public uint cbSize;
            public nint hwnd;
            public uint dwFlags;
            public uint uCount;
            public uint dwTimeout;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DWM_BLURBEHIND
        {
            public uint dwFlags;
            public int fEnable;                // BOOL marshalled as int (0 or 1)
            public nint hRgnBlur;
            public int fTransitionOnMaximized; // BOOL marshalled as int (0 or 1)
        }

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool EnableWindow(nint hWnd, [MarshalAs(UnmanagedType.Bool)] bool bEnable);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool SetForegroundWindow(nint hWnd);

        [LibraryImport("user32.dll")]
        public static partial nint GetForegroundWindow();

        [LibraryImport("user32.dll")]
        public static partial uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool BringWindowToTop(nint hWnd);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool ShowWindow(nint hWnd, int nCmdShow);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool FlashWindowEx(in FLASHWINFO pwfi);

        [LibraryImport("kernel32.dll")]
        public static partial uint GetCurrentThreadId();

        [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
        public static partial int GetWindowLong(nint hWnd, int nIndex);

        [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW")]
        public static partial int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool SetLayeredWindowAttributes(nint hwnd, uint crKey, byte bAlpha, uint dwFlags);

        [LibraryImport("user32.dll")]
        public static partial int GetSystemMetrics(int nIndex);

        [LibraryImport("dwmapi.dll")]
        public static partial int DwmSetWindowAttribute(
            nint hwnd,
            DWMWINDOWATTRIBUTE dwAttribute,
            ref int pvAttribute,
            int cbAttribute);

        [LibraryImport("dwmapi.dll")]
        public static partial int DwmEnableBlurBehindWindow(nint hWnd, ref DWM_BLURBEHIND pBlurBehind);

        [LibraryImport("gdi32.dll")]
        public static partial nint CreateRectRgn(int left, int top, int right, int bottom);

        [LibraryImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool DeleteObject(nint hObject);
    }
}
