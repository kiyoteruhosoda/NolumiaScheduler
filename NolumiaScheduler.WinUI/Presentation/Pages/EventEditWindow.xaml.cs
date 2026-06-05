using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Navigation;
using NolumiaScheduler.Presentation.Resources.Strings;
using Windows.Graphics;

namespace NolumiaScheduler.WinUI.Presentation.Pages;

public sealed partial class EventEditWindow : Window
{
    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    private const int GWLP_HWNDPARENT = -8;

    public EventEditWindow(EventEditParams p)
    {
        InitializeComponent();

        // Apply the Mica backdrop from code-behind rather than XAML. Setting
        // Window.SystemBackdrop via XAML mirrors MainWindow and keeps the backdrop
        // initialization consistent and guarded for systems without Mica support.
        if (Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported())
        {
            SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
        }

        Title = p.EventId != null ? AppResources.EditEventTitle : AppResources.NewEventTitle;

        const int w = 500, h = 760;
        AppWindow.Resize(new SizeInt32(w, h));

        if (App.MainWindow?.AppWindow is { } main)
        {
            var pos  = main.Position;
            var size = main.Size;
            AppWindow.Move(new PointInt32(
                pos.X + (size.Width  - w) / 2,
                pos.Y + (size.Height - h) / 2));
        }

        // Set MainWindow as Win32 owner so this window always appears above it,
        // while still being draggable outside the main window bounds.
        if (App.MainWindow != null)
        {
            var editHwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var mainHwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            SetWindowLongPtr(editHwnd, GWLP_HWNDPARENT, mainHwnd);
        }

        EditFrame.Navigated += OnFrameNavigated;
        EditFrame.Navigate(typeof(EventEditPage), p);
    }

    private void OnFrameNavigated(object sender, NavigationEventArgs e)
    {
        if (e.Content is EventEditPage page)
            page.DismissAction = Close;
    }
}
