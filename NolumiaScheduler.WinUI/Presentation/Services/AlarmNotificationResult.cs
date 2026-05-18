namespace NolumiaScheduler.Presentation.Services;

public enum AlarmNotificationResult
{
    Dismiss,
    Snooze5Min,
    Snooze1Min,
    CancelAll,
    SnoozeTo5MinBefore,
    SnoozeTo1MinBefore
}
