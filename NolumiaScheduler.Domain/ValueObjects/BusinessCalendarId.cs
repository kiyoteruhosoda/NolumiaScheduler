using System;
using System.Collections.Generic;
using System.Text;

namespace NolumiaScheduler.Domain.ValueObjects;

public class BusinessCalendarId(string value)
{
    public string Value { get; } = value;
}
