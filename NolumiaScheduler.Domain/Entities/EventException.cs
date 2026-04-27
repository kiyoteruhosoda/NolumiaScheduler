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

    public sealed class ExceptionOverride
    {
        public EventTitle? Title { get; }
        public Location? Location { get; }
        public Visibility? Visibility { get; }
        public LocalTimeValue? StartTime { get; }
        public LocalTimeValue? EndTime { get; }

        public ExceptionOverride(
            EventTitle? title = null,
            Location? location = null,
            Visibility? visibility = null,
            LocalTimeValue? startTime = null,
            LocalTimeValue? endTime = null)
        {
            Title = title;
            Location = location;
            Visibility = visibility;
            StartTime = startTime;
            EndTime = endTime;
        }

        public bool IsEmpty()
        {
            return Title == null && Location == null && Visibility == null && StartTime == null && EndTime == null;
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
