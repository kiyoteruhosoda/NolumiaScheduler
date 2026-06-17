using NolumiaScheduler.Domain.Exceptions;

namespace NolumiaScheduler.Domain.ValueObjects
{
    /// <summary>
    /// Single (non-recurring) schedule held as DTSTART (local wall-clock) + DURATION, per the
    /// time model in <c>docs/time-model.md</c>. The absolute instant is derived on demand using
    /// the owning event's <see cref="TimeZoneId"/>; the end is always <c>start + duration</c> and
    /// is never stored. Crossing midnight (and spanning multiple days) is allowed.
    /// </summary>
    public sealed class SingleEventSchedule
    {
        public LocalDateValue StartDate { get; }
        public LocalTimeValue StartTime { get; }
        public int DurationMinutes { get; }

        public SingleEventSchedule(LocalDateValue startDate, LocalTimeValue startTime, int durationMinutes)
        {
            if (startDate == null) throw new ArgumentNullException(nameof(startDate));
            if (startTime == null) throw new ArgumentNullException(nameof(startTime));
            if (durationMinutes <= 0) throw new DomainException("durationMinutes must be greater than zero.");

            StartDate = startDate;
            StartTime = startTime;
            DurationMinutes = durationMinutes;
        }
    }
}
