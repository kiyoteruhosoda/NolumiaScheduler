using System;
using System.Collections.Generic;
using System.Text;

namespace NolumiaScheduler.Domain.ValueObjects
{
    public sealed class WeeklyRule
    {
        public IReadOnlyList<Weekday> Weekdays { get; }

        public WeeklyRule(IReadOnlyList<Weekday> weekdays)
        {
            if (weekdays == null || weekdays.Count == 0)
                throw new ArgumentException("At least one weekday is required.");
            Weekdays = weekdays;
        }
    }
}
