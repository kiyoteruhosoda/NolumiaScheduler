namespace NolumiaScheduler.Domain.ValueObjects;

public sealed class TimeZoneId
{
    public string Value { get; }

    public TimeZoneId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("TimeZone ID must not be empty.");

        // Validate that the timezone exists
        _ = TimeZoneInfo.FindSystemTimeZoneById(value);
        Value = value;
    }

    public TimeZoneInfo ToTimeZoneInfo() => TimeZoneInfo.FindSystemTimeZoneById(Value);

    public override bool Equals(object? obj) => obj is TimeZoneId other && Value == other.Value;
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => Value;
}
