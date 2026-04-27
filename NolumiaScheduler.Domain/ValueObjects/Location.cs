using System;
using System.Collections.Generic;
using System.Text;

namespace NolumiaScheduler.Domain.ValueObjects;

public class Location(string value)
{
    public string Value { get; } = value;
}