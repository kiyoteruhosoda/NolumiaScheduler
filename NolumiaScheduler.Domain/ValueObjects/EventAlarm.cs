namespace NolumiaScheduler.Domain.ValueObjects;

public record EventAlarm(
    bool IsEnabled,
    bool Notify15Min,
    bool Notify5Min,
    bool Notify1Min,
    bool NotifyAtStart = true)
{
    public static EventAlarm Default => new(true, true, true, true, true);
}
