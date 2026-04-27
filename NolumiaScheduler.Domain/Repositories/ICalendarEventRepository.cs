using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.ValueObjects;
using System;
using System.Collections.Generic;
using System.Text;

namespace NolumiaScheduler.Domain.Repositories;

public interface ICalendarEventRepository
{
    CalendarEvent? FindById(EventId id);
    IReadOnlyList<CalendarEvent> FindAll();
    void Save(CalendarEvent calendarEvent);
    void Delete(EventId id);
}
