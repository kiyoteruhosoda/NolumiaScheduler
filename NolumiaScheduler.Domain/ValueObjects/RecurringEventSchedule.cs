using NolumiaScheduler.Domain.Exceptions;

namespace NolumiaScheduler.Domain.ValueObjects
{
    /// <summary>
    /// Recurring schedule held as DTSTART (local wall-clock) + DURATION + recurrence rule, per the
    /// time model in <c>docs/time-model.md</c>. Each occurrence's start is built in the owning
    /// event's timezone (wall-clock anchoring) and its end is <c>start + duration</c>. The end is
    /// never stored, and occurrences may cross midnight. All-day is not a domain concept: an
    /// "all-day" recurrence is simply <c>StartTime 00:00 + DurationMinutes 1440</c>.
    /// </summary>
    public sealed class RecurringEventSchedule
    {
        public LocalDateValue StartDate { get; }
        public LocalTimeValue StartTime { get; }
        public int DurationMinutes { get; }
        public RecurrenceRule RecurrenceRule { get; }

        public RecurringEventSchedule(
            LocalDateValue startDate,
            LocalTimeValue startTime,
            int durationMinutes,
            RecurrenceRule recurrenceRule)
        {
            if (startDate == null) throw new ArgumentNullException(nameof(startDate));
            if (startTime == null) throw new ArgumentNullException(nameof(startTime));
            if (recurrenceRule == null) throw new ArgumentNullException(nameof(recurrenceRule));
            if (durationMinutes <= 0) throw new DomainException("durationMinutes must be greater than zero.");

            if (startDate.CompareTo(recurrenceRule.EndDate) > 0)
                throw new DomainException("startDate must be on or before recurrence endDate.");

            StartDate = startDate;
            StartTime = startTime;
            DurationMinutes = durationMinutes;
            RecurrenceRule = recurrenceRule;
        }

        public RecurringEventSchedule WithRecurrenceRule(RecurrenceRule newRule)
        {
            return new RecurringEventSchedule(StartDate, StartTime, DurationMinutes, newRule);
        }
    }
}
