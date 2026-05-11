namespace NolumiaScheduler.Domain.ValueObjects;

public record EventAlarm(
    bool IsEnabled,
    bool Notify15Min,
    bool Notify5Min,
    bool Notify1Min)
{
    public static EventAlarm Default => new(true, true, true, true);
}
