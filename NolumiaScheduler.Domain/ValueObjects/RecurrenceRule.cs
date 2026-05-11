using System;
using System.Collections.Generic;
using System.Text;

namespace NolumiaScheduler.Domain.ValueObjects
{
    public sealed class RecurrenceRule
    {
        public RecurrenceType RuleType { get; }
        public int Interval { get; }
        public LocalDateValue EndDate { get; }
        public WeeklyRule? Weekly { get; }
        public MonthlyRule? Monthly { get; }
        public YearlyRule? Yearly { get; }
        public AdjustmentRule? Adjustment { get; }

        public RecurrenceRule(
            RecurrenceType ruleType,
            int interval,
            LocalDateValue endDate,
            WeeklyRule? weekly = null,
            MonthlyRule? monthly = null,
            YearlyRule? yearly = null,
            AdjustmentRule? adjustment = null)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(interval, 1);

            switch (ruleType)
            {
                case RecurrenceType.Weekly when weekly == null:
                    throw new ArgumentException("Weekly rule is required for weekly recurrence.");
                case RecurrenceType.Monthly when monthly == null:
                    throw new ArgumentException("Monthly rule is required for monthly recurrence.");
                case RecurrenceType.Yearly when yearly == null:
                    throw new ArgumentException("Yearly rule is required for yearly recurrence.");
            }

            RuleType = ruleType;
            Interval = interval;
            EndDate = endDate ?? throw new ArgumentNullException(nameof(endDate));
            Weekly = weekly;
            Monthly = monthly;
            Yearly = yearly;
            Adjustment = adjustment;
        }

        public RecurrenceRule WithEndDate(LocalDateValue newEndDate)
        {
            return new RecurrenceRule(RuleType, Interval, newEndDate, Weekly, Monthly, Yearly, Adjustment);
        }
    }
}
