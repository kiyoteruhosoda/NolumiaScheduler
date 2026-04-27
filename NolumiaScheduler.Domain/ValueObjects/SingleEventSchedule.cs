using NolumiaScheduler.Domain.Exceptions;
using System;
using System.Collections.Generic;
using System.Text;

namespace NolumiaScheduler.Domain.ValueObjects
{
    public sealed class SingleEventSchedule
    {
        public DateTimeOffset Start { get; }
        public DateTimeOffset End { get; }

        public SingleEventSchedule(DateTimeOffset start, DateTimeOffset end)
        {
            if (start >= end) throw new DomainException("start must be before end.");
            Start = start;
            End = end;
        }
    }
}
