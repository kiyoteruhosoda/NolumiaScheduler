namespace NolumiaScheduler.Domain.ValueObjects;

public sealed class LocalDateValue : IEquatable<LocalDateValue>, IComparable<LocalDateValue>
{
    public int Year { get; }
    public int Month { get; }
    public int Day { get; }

    public LocalDateValue(int year, int month, int day)
    {
        if (year < 1)
            throw new ArgumentOutOfRangeException(nameof(year), year, "Year must be greater than 0.");

        if (month < 1 || month > 12)
            throw new ArgumentOutOfRangeException(nameof(month), month, "Month must be between 1 and 12.");

        if (day < 1 || day > DateTime.DaysInMonth(year, month))
            throw new ArgumentOutOfRangeException(nameof(day), day, "Day is out of range for the specified year and month.");

        Year = year;
        Month = month;
        Day = day;
    }

    public DateOnly ToDateOnly() => new(Year, Month, Day);

    public static LocalDateValue FromDateOnly(DateOnly date) => new(date.Year, date.Month, date.Day);

    public LocalDateValue AddDays(int days) => FromDateOnly(ToDateOnly().AddDays(days));
    public LocalDateValue AddMonths(int months) => FromDateOnly(ToDateOnly().AddMonths(months));
    public LocalDateValue AddYears(int years) => FromDateOnly(ToDateOnly().AddYears(years));

    public DayOfWeek DayOfWeek => ToDateOnly().DayOfWeek;

    public int CompareTo(LocalDateValue? other)
    {
        if (other is null) return 1;
        return ToDateOnly().CompareTo(other.ToDateOnly());
    }

    public bool Equals(LocalDateValue? other) => other is not null && Year == other.Year && Month == other.Month && Day == other.Day;
    public override bool Equals(object? obj) => Equals(obj as LocalDateValue);
    public override int GetHashCode() => HashCode.Combine(Year, Month, Day);
    public override string ToString() => $"{Year:D4}-{Month:D2}-{Day:D2}";

    public static bool operator ==(LocalDateValue? a, LocalDateValue? b) => Equals(a, b);
    public static bool operator !=(LocalDateValue? a, LocalDateValue? b) => !Equals(a, b);
    public static bool operator <(LocalDateValue a, LocalDateValue b) => a.CompareTo(b) < 0;
    public static bool operator >(LocalDateValue a, LocalDateValue b) => a.CompareTo(b) > 0;
    public static bool operator <=(LocalDateValue a, LocalDateValue b) => a.CompareTo(b) <= 0;
    public static bool operator >=(LocalDateValue a, LocalDateValue b) => a.CompareTo(b) >= 0;
}
