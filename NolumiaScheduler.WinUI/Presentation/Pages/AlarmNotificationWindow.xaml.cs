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

    public AlarmNotificationWindow(string title, string message, string? location = null, DateTime? eventStartTime = null)
    {
        InitializeComponent();

        // Set all text from resources
        Title = AppResources.AlarmWindowTitle;
        TitleLabel.Text = title;
        MessageLabel.Text = message;
        TimeLabel.Text = DateTime.Now.ToString("yyyy/MM/dd HH:mm");
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
        if (eventStartTime.HasValue && eventStartTime.Value > DateTime.Now)
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

        // Force to foreground and pin as topmost so the alarm is always visible
        var hwnd = WindowNative.GetWindowHandle(this);
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);
        NativeMethods.SetForegroundWindow(hwnd);

        // Disable other app windows to simulate system-modal
        DisableOtherWindows(true);

        Closed += (_, _) =>
        {
            DisableOtherWindows(false);
            _tcs.TrySetResult(AlarmNotificationResult.Dismiss);
        };
    }

    public Task<AlarmNotificationResult> WaitForResultAsync() => _tcs.Task;

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

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool EnableWindow(nint hWnd, [MarshalAs(UnmanagedType.Bool)] bool bEnable);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool SetForegroundWindow(nint hWnd);
    }
}
