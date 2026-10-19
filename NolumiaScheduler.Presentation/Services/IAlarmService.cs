using NolumiaScheduler.Application.Services;

namespace NolumiaScheduler.Presentation.Services;

/// <summary>
/// Presentation-side alarm host: runs the polling timer and shows notification windows.
/// All due/fired/snooze decisions live in <see cref="AlarmApplicationService"/>.
/// </summary>
public interface IAlarmService
{
    void Start();
    void Stop();
    void ResetFiredKeys(string eventId);

    /// <summary>
    /// Raised when the alarm schedule has changed (event added/deleted/modified).
    /// </summary>
    event Action? ScheduleChanged;
}
