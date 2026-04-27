using System;
using System.Collections.Generic;
using System.Text;

namespace NolumiaScheduler.Domain.ValueObjects
{
    public abstract class YearlyRule { }

    public sealed class DayOfMonthYearlyRule : YearlyRule
    {
        public int Month { get; }
        public int Day { get; }

        public DayOfMonthYearlyRule(int month, int day)
        {
            if (month < 1 || month > 12) throw new ArgumentOutOfRangeException(nameof(month));
            if (day < 1 || day > 31) throw new ArgumentOutOfRangeException(nameof(day));
            Month = month;
            Day = day;
        }
    }

    public sealed class NthWeekdayYearlyRule : YearlyRule
    {
        public int Month { get; }
        public int WeekIndex { get; }
        public Weekday Weekday { get; }

        public NthWeekdayYearlyRule(int month, int weekIndex, Weekday weekday)
        {
            if (month < 1 || month > 12) throw new ArgumentOutOfRangeException(nameof(month));
            if (weekIndex < -1 || weekIndex == 0 || weekIndex > 5) throw new ArgumentOutOfRangeException(nameof(weekIndex));
            Month = month;
            WeekIndex = weekIndex;
            Weekday = weekday;
        }
    }
}
