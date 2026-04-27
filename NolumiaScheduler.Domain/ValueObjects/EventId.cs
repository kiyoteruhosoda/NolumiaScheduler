using System;
using System.Collections.Generic;
using System.Text;

namespace NolumiaScheduler.Domain.ValueObjects;

public class EventId(string value)
{
    public string Value { get; } = value;
}