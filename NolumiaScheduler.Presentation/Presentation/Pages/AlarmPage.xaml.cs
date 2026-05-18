using NolumiaScheduler.Resources.Strings;
using MauiApp = Microsoft.Maui.Controls.Application;

namespace NolumiaScheduler.Presentation.Pages;

public partial class AlarmPage : Window
{
    private readonly TaskCompletionSource<AlarmAction> _tcs = new();

    public AlarmPage(string title, string message)
    {
        InitializeComponent();

        TitleLabel.Text = title;
        MessageLabel.Text = message;
        Snooze5Button.Text = AppResources.AlarmSnooze5Min;
        Snooze1Button.Text = AppResources.AlarmSnooze1Min;
        DismissButton.Text = AppResources.AlarmDismiss;
    }

    public Task<AlarmAction> WaitForActionAsync() => _tcs.Task;

    private void OnSnooze5Clicked(object? sender, EventArgs e)
    {
        _tcs.TrySetResult(AlarmAction.Snooze5Min);
        CloseWindow();
    }

    private void OnSnooze1Clicked(object? sender, EventArgs e)
    {
        _tcs.TrySetResult(AlarmAction.Snooze1Min);
        CloseWindow();
    }

    private void OnDismissClicked(object? sender, EventArgs e)
    {
        _tcs.TrySetResult(AlarmAction.Dismiss);
        CloseWindow();
    }

    private void CloseWindow()
    {
        MauiApp.Current?.CloseWindow(this);
    }
}

public enum AlarmAction
{
    Dismiss,
    Snooze5Min,
    Snooze1Min
}
