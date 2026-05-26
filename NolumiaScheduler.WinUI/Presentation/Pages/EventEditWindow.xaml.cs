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

        AppWindow.Resize(new SizeInt32(500, 760));

        EditFrame.Navigated += OnFrameNavigated;
        EditFrame.Navigate(typeof(EventEditPage), p);
    }

    private void OnFrameNavigated(object sender, NavigationEventArgs e)
    {
        if (e.Content is EventEditPage page)
            page.DismissAction = Close;
    }
}
