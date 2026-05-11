namespace NolumiaScheduler.Domain.ValueObjects;

public sealed class Description(string value)
{
    public string Value { get; } = value ?? string.Empty;

    public override bool Equals(object? obj) => obj is Description other && Value == other.Value;
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => Value;
}
