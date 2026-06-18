using NolumiaScheduler.Domain.Exceptions;

namespace NolumiaScheduler.Domain.ValueObjects
{
    /// <summary>
    /// Single (non-recurring) schedule held as an absolute UTC instant + duration, per the time
    /// model in <c>docs/time-model.md</c> §3. The owning event's <see cref="TimeZoneId"/> is
    /// display/metadata only; the instant never re-resolves through the tz database. The end is
    /// always <c>StartUtc + duration</c> and is never stored.
    /// </summary>
    public sealed class SingleEventSchedule
    {
        public DateTimeOffset StartUtc { get; }
        public int DurationMinutes { get; }

        public SingleEventSchedule(DateTimeOffset startUtc, int durationMinutes)
        {
            if (durationMinutes <= 0) throw new DomainException("durationMinutes must be greater than zero.");

            StartUtc = startUtc.ToUniversalTime();
            DurationMinutes = durationMinutes;
        }

        public DateTimeOffset EndUtc => StartUtc.AddMinutes(DurationMinutes);
    }
}
