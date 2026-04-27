using NolumiaScheduler.Domain.Exceptions;
using System;
using System.Collections.Generic;
using System.Text;

namespace NolumiaScheduler.Domain.ValueObjects
{
    public sealed class RecurringEventSchedule
    {
        public LocalDateValue StartDate { get; }
        public LocalTimeValue? StartTime { get; }
        public LocalTimeValue? EndTime { get; }
        public RecurrenceRule RecurrenceRule { get; }
        public bool AllDay { get; }

        public RecurringEventSchedule(
            LocalDateValue startDate,
            LocalTimeValue? startTime,
            LocalTimeValue? endTime,
            RecurrenceRule recurrenceRule,
            bool allDay)
        {
            if (allDay)
            {
                if (startTime != null || endTime != null)
                    throw new DomainException("All-day recurring event must not have times.");
            }
            else
            {
                if (startTime == null || endTime == null)
                    throw new DomainException("Timed recurring event requires start/end time.");

                if (startTime.CompareTo(endTime) >= 0)
                    throw new DomainException("Cross-day recurring timed event is not allowed.");
            }

            if (startDate.CompareTo(recurrenceRule.EndDate) > 0)
                throw new DomainException("startDate must be on or before recurrence endDate.");

            StartDate = startDate;
            StartTime = startTime;
            EndTime = endTime;
            RecurrenceRule = recurrenceRule;
            AllDay = allDay;
        }

        public RecurringEventSchedule WithRecurrenceRule(RecurrenceRule newRule)
        {
            return new RecurringEventSchedule(StartDate, StartTime, EndTime, newRule, AllDay);
        }
    }
}
