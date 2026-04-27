namespace NolumiaScheduler.Domain.ValueObjects;

public enum AdjustmentDirection
{
    Forward,
    Backward
}

public sealed class AdjustmentRule
{
    public AdjustmentDirection Direction { get; }

    public AdjustmentRule(AdjustmentDirection direction)
    {
        Direction = direction;
    }
}
