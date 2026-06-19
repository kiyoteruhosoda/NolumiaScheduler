using NolumiaScheduler.Domain.ValueObjects;
using System;
using System.Collections.Generic;
using System.Text;

namespace NolumiaScheduler.Domain.Entities
{
    public sealed class EventMove(
        OccurrenceLocalKey occurrenceKey,
        LocalDateValue newDate,
        LocalTimeValue? newStartTime = null,
        int? newDurationMinutes = null,
        EventTitle? title = null,
        Location? location = null,
        Visibility? visibility = null)
    {
        public OccurrenceLocalKey OccurrenceKey { get; } = occurrenceKey ?? throw new ArgumentNullException(nameof(occurrenceKey));
        public LocalDateValue NewDate { get; } = newDate ?? throw new ArgumentNullException(nameof(newDate));
        public LocalTimeValue? NewStartTime { get; } = newStartTime;
        public int? NewDurationMinutes { get; } = newDurationMinutes;
        public EventTitle? Title { get; } = title;
        public Location? Location { get; } = location;
        public Visibility? Visibility { get; } = visibility;
    }
}
