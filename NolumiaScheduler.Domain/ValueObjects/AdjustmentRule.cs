namespace NolumiaScheduler.Domain.ValueObjects;

public enum AdjustmentDirection
{
    Forward,
    Backward
}

public enum AdjustmentCondition
{
    Holiday
}

public enum AdjustmentShiftUnit
{
    BusinessDay,
    CalendarDay
}

public sealed class AdjustmentRule(
    AdjustmentCondition condition,
    AdjustmentShiftUnit shiftUnit,
    int shiftAmount,
    BusinessCalendarId? calendarId = null)
{
    public AdjustmentCondition Condition { get; } = condition;
    public AdjustmentShiftUnit ShiftUnit { get; } = shiftUnit;
    public int ShiftAmount { get; } = shiftAmount;
    public BusinessCalendarId? CalendarId { get; } = calendarId;

    public AdjustmentDirection Direction
        => ShiftAmount < 0 ? AdjustmentDirection.Backward : AdjustmentDirection.Forward;

    // Back-compat: HOLIDAY + BUSINESS_DAY + ±1.
    public AdjustmentRule(AdjustmentDirection direction)
        : this(
            AdjustmentCondition.Holiday,
            AdjustmentShiftUnit.BusinessDay,
            direction == AdjustmentDirection.Backward ? -1 : 1)
    {
    }
}
