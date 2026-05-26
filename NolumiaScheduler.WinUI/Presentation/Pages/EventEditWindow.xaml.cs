using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Navigation;
using NolumiaScheduler.Presentation.Resources.Strings;
using Windows.Graphics;

namespace NolumiaScheduler.WinUI.Presentation.Pages;

public sealed partial class EventEditWindow : Window
{
    public EventEditWindow(EventEditParams p)
    {
        InitializeComponent();

        Title = p.EventId != null ? AppResources.EditEventTitle : AppResources.NewEventTitle;

        const int w = 500, h = 760;
        AppWindow.Resize(new SizeInt32(w, h));

        if (App.MainWindow?.AppWindow is { } main)
        {
            var pos  = main.Position;
            var size = main.Size;
            AppWindow.Move(new Windows.Graphics.PointInt32(
                pos.X + (size.Width  - w) / 2,
                pos.Y + (size.Height - h) / 2));
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
