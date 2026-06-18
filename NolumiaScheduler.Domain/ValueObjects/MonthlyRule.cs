using System;
using System.Collections.Generic;
using System.Text;

namespace NolumiaScheduler.Domain.ValueObjects
{
    public abstract class MonthlyRule { }

    public sealed class DayOfMonthMonthlyRule : MonthlyRule
    {
        public int Day { get; }

        public DayOfMonthMonthlyRule(int day)
        {
            if (day < 1 || day > 31)
                throw new ArgumentOutOfRangeException(nameof(day));
            Day = day;
        }
    }

    public sealed class NthWeekdayMonthlyRule : MonthlyRule
    {
        public int WeekIndex { get; }   // -1, 1..5
        public Weekday Weekday { get; }

        public NthWeekdayMonthlyRule(int weekIndex, Weekday weekday)
        {
            if (weekIndex < -1 || weekIndex == 0 || weekIndex > 5)
                throw new ArgumentOutOfRangeException(nameof(weekIndex));
            WeekIndex = weekIndex;
            Weekday = weekday;
        }
    }

    /// <summary>The last calendar day of each month (28–31), e.g. "毎月末".</summary>
    public sealed class LastDayOfMonthMonthlyRule : MonthlyRule
    {
    }
}
