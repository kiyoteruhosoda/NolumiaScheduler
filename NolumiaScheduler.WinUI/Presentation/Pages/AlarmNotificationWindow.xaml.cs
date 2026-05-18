using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using NolumiaScheduler.Presentation.Services;
using System.Diagnostics;
using System.Runtime.InteropServices;
using WinRT.Interop;

namespace NolumiaScheduler.Presentation.Pages;

public sealed partial class AlarmNotificationWindow : Window
{
    private readonly TaskCompletionSource<AlarmNotificationResult> _tcs = new();
    private readonly string? _location;

    public AlarmNotificationWindow(string title, string message, string? location = null)
    {
        InitializeComponent();

        TitleLabel.Text = title;
        MessageLabel.Text = message;
        TimeLabel.Text = DateTime.Now.ToString("yyyy/MM/dd HH:mm");
        _location = location;

        // Show location button if location is provided
        if (!string.IsNullOrWhiteSpace(location))
        {
            OpenLocationBtn.Visibility = Visibility.Visible;
        }

        // Configure window: full-screen, no titlebar
        if (AppWindow is not null)
        {
            ExtendsContentIntoTitleBar = true;
            AppWindow.TitleBar?.PreferredHeightOption = TitleBarHeightOption.Collapsed;
            AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
        }

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

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnableWindow(nint hWnd, [MarshalAs(UnmanagedType.Bool)] bool bEnable);
    }
}
