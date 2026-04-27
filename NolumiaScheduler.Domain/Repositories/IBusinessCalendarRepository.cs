using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.ValueObjects;
using System;
using System.Collections.Generic;
using System.Text;

namespace NolumiaScheduler.Domain.Repositories
{
    public interface IBusinessCalendarRepository
    {
        BusinessCalendar? FindById(BusinessCalendarId id);
        IReadOnlyList<BusinessCalendar> FindAll();
        void Save(BusinessCalendar calendar);
    }
}
