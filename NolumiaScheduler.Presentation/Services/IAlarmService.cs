using NolumiaScheduler.Application.Services;

namespace NolumiaScheduler.Presentation.Services;

/// <summary>
/// Presentation-side alarm host: runs the polling timer and shows notification windows.
/// All due/fired/snooze decisions live in <see cref="AlarmApplicationService"/>; the members
/// below that expose schedule state simply delegate to it for debug/diagnostic UI.
/// </summary>
public interface IAlarmService
{
    void Start();
    void Stop();
    void ResetFiredKeys(string eventId);
    IReadOnlyList<AlarmScheduleEntry> GetScheduledAlarms();
    IReadOnlyList<string> GetFiredKeys();
    Task ShowTestAlarmAsync();

    /// <summary>
    /// Raised when the alarm schedule has changed (event added/deleted/modified).
    /// </summary>
    event Action? ScheduleChanged;

    /// <summary>
    /// Returns detailed diagnostic lines explaining why alarms are/aren't scheduled.
    /// </summary>
    IReadOnlyList<string> GetDiagnosticLines();
}
