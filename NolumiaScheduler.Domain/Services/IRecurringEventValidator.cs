using NolumiaScheduler.Domain.Aggregates;
using System;
using System.Collections.Generic;
using System.Text;

namespace NolumiaScheduler.Domain.Services
{
    public interface IRecurringEventValidator
    {
        void Validate(CalendarEvent calendarEvent);
    }
}
