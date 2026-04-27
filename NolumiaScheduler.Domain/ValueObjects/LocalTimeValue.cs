namespace NolumiaScheduler.Domain.ValueObjects;

public sealed class LocalTimeValue : IEquatable<LocalTimeValue>, IComparable<LocalTimeValue>
{
    public int Hour { get; }
    public int Minute { get; }
    public int Second { get; }

    public LocalTimeValue(int hour, int minute, int second = 0)
    {
        if (hour < 0 || hour > 23 || minute < 0 || minute > 59 || second < 0 || second > 59)
            throw new ArgumentOutOfRangeException("Invalid time components.");

        Hour = hour;
        Minute = minute;
        Second = second;
    }

    public TimeOnly ToTimeOnly() => new(Hour, Minute, Second);

    public static LocalTimeValue FromTimeOnly(TimeOnly time) => new(time.Hour, time.Minute, time.Second);

    public int CompareTo(LocalTimeValue? other)
    {
        if (other is null) return 1;
        return ToTimeOnly().CompareTo(other.ToTimeOnly());
    }

    public bool Equals(LocalTimeValue? other) => other is not null && Hour == other.Hour && Minute == other.Minute && Second == other.Second;
    public override bool Equals(object? obj) => Equals(obj as LocalTimeValue);
    public override int GetHashCode() => HashCode.Combine(Hour, Minute, Second);
    public override string ToString() => $"{Hour:D2}:{Minute:D2}:{Second:D2}";
}
