namespace NolumiaScheduler.Domain.ValueObjects;

public sealed class VersionNo
{
    public int Value { get; }

    public VersionNo(int value)
    {
        if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
        Value = value;
    }

    public VersionNo Next() => new(Value + 1);

    public static VersionNo Initial() => new(1);

    public override bool Equals(object? obj) => obj is VersionNo other && Value == other.Value;
    public override int GetHashCode() => Value.GetHashCode();
}
