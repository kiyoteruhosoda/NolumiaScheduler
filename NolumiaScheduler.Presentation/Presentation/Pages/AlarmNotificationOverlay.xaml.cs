namespace NolumiaScheduler.Presentation.Pages;

public partial class AlarmNotificationOverlay : ContentView
{
    private readonly TaskCompletionSource<AlarmNotificationResult> _tcs = new();

    public AlarmNotificationOverlay(string title, string message)
    {
        InitializeComponent();
        TitleLabel.Text = title;
        MessageLabel.Text = message;
        TimeLabel.Text = DateTime.Now.ToString("yyyy/MM/dd HH:mm");
    }

    public Task<AlarmNotificationResult> WaitForResultAsync() => _tcs.Task;

    private void Complete(AlarmNotificationResult result)
    {
        _tcs.TrySetResult(result);
    }

    private void OnDismissClicked(object? sender, EventArgs e)
        => Complete(AlarmNotificationResult.Dismiss);

    private void OnSnooze5Clicked(object? sender, EventArgs e)
        => Complete(AlarmNotificationResult.Snooze5Min);

    private void OnSnooze1Clicked(object? sender, EventArgs e)
        => Complete(AlarmNotificationResult.Snooze1Min);
}

public enum AlarmNotificationResult
{
    Dismiss,
    Snooze5Min,
    Snooze1Min,
    CancelAll,
    SnoozeTo5MinBefore,
    SnoozeTo1MinBefore
}
