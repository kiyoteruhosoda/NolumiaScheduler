using System;
using System.Collections.Generic;
using System.Text;

namespace NolumiaScheduler.Domain.ValueObjects
{
    public sealed class Holiday(LocalDateValue date, string? name = null)
    {
        public LocalDateValue Date { get; } = date ?? throw new ArgumentNullException(nameof(date));
        public string? Name { get; } = name;
    }
}
