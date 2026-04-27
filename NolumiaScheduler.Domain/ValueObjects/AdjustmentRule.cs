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

public sealed class AdjustmentRule
{
    public AdjustmentCondition Condition { get; }
    public AdjustmentShiftUnit ShiftUnit { get; }
    public int ShiftAmount { get; }
    public BusinessCalendarId? CalendarId { get; }

    public AdjustmentDirection Direction
        => ShiftAmount < 0 ? AdjustmentDirection.Backward : AdjustmentDirection.Forward;

    public AdjustmentRule(
        AdjustmentCondition condition,
        AdjustmentShiftUnit shiftUnit,
        int shiftAmount,
        BusinessCalendarId? calendarId = null)
    {
        Condition = condition;
        ShiftUnit = shiftUnit;
        ShiftAmount = shiftAmount;
        CalendarId = calendarId;
    }

    // Back-compat: HOLIDAY + BUSINESS_DAY + ±1.
    public AdjustmentRule(AdjustmentDirection direction)
        : this(
            AdjustmentCondition.Holiday,
            AdjustmentShiftUnit.BusinessDay,
            direction == AdjustmentDirection.Backward ? -1 : 1)
    {
    }
}
