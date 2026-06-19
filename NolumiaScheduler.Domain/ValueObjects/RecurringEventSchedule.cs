using NolumiaScheduler.Domain.Exceptions;

namespace NolumiaScheduler.Domain.ValueObjects
{
    /// <summary>
    /// Recurring schedule held as an absolute UTC anchor instant + duration + recurrence rule, per
    /// the time model in <c>docs/time-model.md</c> §3-§4. <see cref="AnchorUtc"/> is the first
    /// occurrence's instant; later occurrences are pinned by integer-day arithmetic from it (see
    /// the occurrence expander), so they never re-resolve through the tz database. All-day is not a
    /// domain concept: a full-day recurrence is simply a midnight-local anchor + 1440 minutes.
    /// </summary>
    public sealed class RecurringEventSchedule
    {
        public DateTimeOffset AnchorUtc { get; }
        public int DurationMinutes { get; }
        public RecurrenceRule RecurrenceRule { get; }

        public RecurringEventSchedule(
            DateTimeOffset anchorUtc,
            int durationMinutes,
            RecurrenceRule recurrenceRule)
        {
            if (recurrenceRule == null) throw new ArgumentNullException(nameof(recurrenceRule));
            if (durationMinutes <= 0) throw new DomainException("durationMinutes must be greater than zero.");

            AnchorUtc = anchorUtc.ToUniversalTime();
            DurationMinutes = durationMinutes;
            RecurrenceRule = recurrenceRule;
        }

        public RecurringEventSchedule WithRecurrenceRule(RecurrenceRule newRule)
        {
            return new RecurringEventSchedule(AnchorUtc, DurationMinutes, newRule);
        }
    }
}
