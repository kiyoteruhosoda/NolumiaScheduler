namespace NolumiaScheduler.WinUI.Presentation.Services;

/// <summary>The action the user took on the alarm notification window.</summary>
public enum AlarmNotificationAction
{
    Dismiss,
    CancelAll,

    /// <summary>Set the next alarm to <see cref="AlarmNotificationResult.Minutes"/> minutes from now.</summary>
    SetNextAlarmFromNow,

    /// <summary>Set the next alarm to <see cref="AlarmNotificationResult.Minutes"/> minutes before the event start.</summary>
    SetNextAlarmBeforeStart
}

/// <summary>
/// Outcome of an alarm notification window. <see cref="Action"/> drives the snooze/cancel behaviour;
/// the <c>Notify5Min</c>/<c>Notify1Min</c> toggle states are always carried so the host can persist
/// the per-event offset settings when <see cref="AlarmSettingsChanged"/> is set.
/// </summary>
public sealed record AlarmNotificationResult
{
    public required AlarmNotificationAction Action { get; init; }

    /// <summary>Minutes for the <c>SetNextAlarm*</c> actions; ignored otherwise.</summary>
    public int Minutes { get; init; }

    public bool Notify5Min { get; init; }
    public bool Notify1Min { get; init; }

    /// <summary>True when the offset toggles differ from the values the window opened with.</summary>
    public bool AlarmSettingsChanged { get; init; }

    public static AlarmNotificationResult Dismissed { get; } = new() { Action = AlarmNotificationAction.Dismiss };
}
