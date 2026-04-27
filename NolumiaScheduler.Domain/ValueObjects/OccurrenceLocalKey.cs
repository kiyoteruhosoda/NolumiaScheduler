using System;
using System.Collections.Generic;
using System.Text;

namespace NolumiaScheduler.Domain.ValueObjects
{
    public sealed class OccurrenceLocalKey : IEquatable<OccurrenceLocalKey>
    {
        public LocalDateValue Date { get; }
        public LocalTimeValue? Time { get; }

        public OccurrenceLocalKey(LocalDateValue date, LocalTimeValue? time = null)
        {
            Date = date ?? throw new ArgumentNullException(nameof(date));
            Time = time;
        }

        public bool Equals(OccurrenceLocalKey? other)
        {
            if (other is null) return false;
            return Date.Equals(other.Date) && Equals(Time, other.Time);
        }

        public override bool Equals(object? obj) => Equals(obj as OccurrenceLocalKey);

        public override int GetHashCode() => HashCode.Combine(Date, Time);
    }
}
