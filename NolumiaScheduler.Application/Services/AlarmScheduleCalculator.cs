using NolumiaScheduler.Domain.ValueObjects;

namespace NolumiaScheduler.Application.Services;

/// <summary>
/// Pure scheduling rules for event alarms, kept out of the platform layer so they can be unit
/// tested. A notification fires at each enabled offset before the event start (15 / 5 / 1 min)
/// and, when enabled, at the event start time itself (offset 0).
/// </summary>
public static class AlarmScheduleCalculator
{
    /// <summary>Catch-up grace after a notify time: lets a delayed timer tick or a just-opened app
    /// still raise a notification whose time just passed. Alarms never fire <em>early</em>.</summary>
    public static readonly TimeSpan CatchUpGrace = TimeSpan.FromMinutes(1);

    /// <summary>Minutes-before offsets, longest first; 0 means "at the event start time".</summary>
    public static readonly IReadOnlyList<int> Offsets = [15, 5, 1, 0];

    /// <summary>Whether the given offset (one of <see cref="Offsets"/>) is enabled for this alarm.</summary>
    public static bool IsOffsetEnabled(EventAlarm alarm, int offsetMinutes) => offsetMinutes switch
    {
        15 => alarm.Notify15Min,
        5  => alarm.Notify5Min,
        1  => alarm.Notify1Min,
        0  => alarm.NotifyAtStart,
        _  => false,
    };

    /// <summary>
    /// True when <paramref name="now"/> has reached <paramref name="notifyAt"/> and is still within
    /// the catch-up grace. This intentionally does not fire before <paramref name="notifyAt"/> so an
    /// alarm rings at the scheduled time rather than up to a couple of minutes early.
    /// </summary>
    public static bool IsDue(DateTime now, DateTime notifyAt)
        => now >= notifyAt && now <= notifyAt.Add(CatchUpGrace);
}
