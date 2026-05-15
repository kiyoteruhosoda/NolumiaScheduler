namespace NolumiaScheduler.Presentation.Services;

public interface IAlarmService
{
    void Start();
    void Stop();
    void ResetFiredKeys(string eventId);
    IReadOnlyList<AlarmScheduleEntry> GetScheduledAlarms();
    IReadOnlyList<string> GetFiredKeys();
    Task ShowTestAlarmAsync();
}

public record AlarmScheduleEntry(
    string EventId,
    string Title,
    DateTime OccurrenceStart,
    int MinutesBefore,
    DateTime NotifyAt,
    bool AlreadyFired,
    bool IsSnoozed);
