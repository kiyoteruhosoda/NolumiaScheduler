using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using NolumiaScheduler.Presentation.Resources.Strings;
using NolumiaScheduler.WinUI.Presentation.Services;
using System.Diagnostics;
using System.Runtime.InteropServices;
using WinRT.Interop;

namespace NolumiaScheduler.WinUI.Presentation.Pages;

public sealed partial class AlarmNotificationWindow : Window
{
    private readonly TaskCompletionSource<AlarmNotificationResult> _tcs = new();
    private readonly string? _location;
    private bool _foregroundForced;

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

        // Configure window: full-screen, no titlebar, always on top
        if (AppWindow is not null)
        {
            // Set taskbar icon
            AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico"));

            ExtendsContentIntoTitleBar = true;
            AppWindow.TitleBar?.PreferredHeightOption = TitleBarHeightOption.Collapsed;
            AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
        }

        // Disable other app windows to simulate system-modal
        DisableOtherWindows(true);

        // Bring to the foreground once the window is actually activated, and re-assert top-most
        // whenever it loses focus so it stays visible (mirrors WPF's maintained Topmost). The
        // foreground push must run after Activate() — doing it in the constructor, before the
        // hosting service calls Activate(), is too early and gets overridden.
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
        public static readonly nint HWND_TOPMOST = -1;
        public const uint SWP_NOMOVE      = 0x0002;
        public const uint SWP_NOSIZE      = 0x0001;
        public const uint SWP_SHOWWINDOW  = 0x0040;
        public const int  SW_SHOW         = 5;
        public const int  SW_RESTORE      = 9;
        public const uint FLASHW_ALL       = 0x00000003;
        public const uint FLASHW_TIMERNOFG = 0x0000000C;

        [StructLayout(LayoutKind.Sequential)]
        public struct FLASHWINFO
        {
            public uint cbSize;
            public nint hwnd;
            public uint dwFlags;
            public uint uCount;
            public uint dwTimeout;
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
    }
}
