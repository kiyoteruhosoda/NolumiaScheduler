using NolumiaScheduler.Domain.ValueObjects;
using System;
using System.Collections.Generic;
using System.Text;

namespace NolumiaScheduler.Domain.Entities
{
    public enum ExceptionType
    {
        Skip,
        Override
    }

    public sealed class ExceptionOverride(
        EventTitle? title = null,
        Location? location = null,
        Visibility? visibility = null,
        LocalTimeValue? startTime = null,
        int? durationMinutes = null)
    {
        public EventTitle? Title { get; } = title;
        public Location? Location { get; } = location;
        public Visibility? Visibility { get; } = visibility;
        public LocalTimeValue? StartTime { get; } = startTime;
        public int? DurationMinutes { get; } = durationMinutes;

        public bool IsEmpty()
        {
            return Title == null && Location == null && Visibility == null && StartTime == null && DurationMinutes == null;
        }
    }

    public sealed class EventException
    {
        public OccurrenceLocalKey OccurrenceKey { get; }
        public ExceptionType Type { get; }
        public ExceptionOverride? Override { get; }

        private EventException(OccurrenceLocalKey occurrenceKey, ExceptionType type, ExceptionOverride? @override)
        {
            OccurrenceKey = occurrenceKey;
            Type = type;
            Override = @override;
        }

        public static EventException CreateSkip(OccurrenceLocalKey occurrenceKey)
        {
            return new EventException(occurrenceKey, ExceptionType.Skip, null);
        }

        public static EventException CreateOverride(OccurrenceLocalKey occurrenceKey, ExceptionOverride exceptionOverride)
        {
            return new EventException(occurrenceKey, ExceptionType.Override, exceptionOverride);
        }
    }
}
