namespace NolumiaScheduler.Domain.ValueObjects;

public enum Weekday
{
    Sunday = 0,
    Monday = 1,
    Tuesday = 2,
    Wednesday = 3,
    Thursday = 4,
    Friday = 5,
    Saturday = 6
}

public static class WeekdayExtensions
{
    public static DayOfWeek ToDayOfWeek(this Weekday weekday) => (DayOfWeek)(int)weekday;

    public static Weekday ToWeekday(this DayOfWeek dayOfWeek) => (Weekday)(int)dayOfWeek;
}
