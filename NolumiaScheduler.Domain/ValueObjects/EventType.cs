namespace NolumiaScheduler.Domain.ValueObjects;

public sealed class EventType
{
    public string Value { get; }

    public EventType(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("EventType must not be empty.");
        Value = value;
    }

    public override bool Equals(object? obj) => obj is EventType other && Value == other.Value;
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => Value;
}
