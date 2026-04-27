using NolumiaScheduler.Domain.ValueObjects;
using System;
using System.Collections.Generic;
using System.Text;

namespace NolumiaScheduler.Domain.Entities
{
    public sealed class EventMove
    {
        public OccurrenceLocalKey OccurrenceKey { get; }
        public LocalDateValue NewDate { get; }
        public LocalTimeValue? NewStartTime { get; }
        public LocalTimeValue? NewEndTime { get; }
        public EventTitle? Title { get; }
        public Location? Location { get; }
        public Visibility? Visibility { get; }

        public EventMove(
            OccurrenceLocalKey occurrenceKey,
            LocalDateValue newDate,
            LocalTimeValue? newStartTime = null,
            LocalTimeValue? newEndTime = null,
            EventTitle? title = null,
            Location? location = null,
            Visibility? visibility = null)
        {
            OccurrenceKey = occurrenceKey ?? throw new ArgumentNullException(nameof(occurrenceKey));
            NewDate = newDate ?? throw new ArgumentNullException(nameof(newDate));
            NewStartTime = newStartTime;
            NewEndTime = newEndTime;
            Title = title;
            Location = location;
            Visibility = visibility;
        }
    }
}
