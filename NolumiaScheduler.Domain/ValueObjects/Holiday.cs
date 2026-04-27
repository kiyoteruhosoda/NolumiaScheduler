using System;
using System.Collections.Generic;
using System.Text;

namespace NolumiaScheduler.Domain.ValueObjects
{
    public sealed class Holiday
    {
        public LocalDateValue Date { get; }
        public string? Name { get; }

        public Holiday(LocalDateValue date, string? name = null)
        {
            Date = date ?? throw new ArgumentNullException(nameof(date));
            Name = name;
        }
    }
}
