using System;
using System.Collections.Generic;
using System.Text;

namespace NolumiaScheduler.Infrastructure.Json.Documents;

public class CalendarEventDocument
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Title { get; set; } = "";
    // ...
}
