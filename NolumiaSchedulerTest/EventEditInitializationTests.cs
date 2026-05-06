using NolumiaScheduler.Application.Services;
using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.Repositories;
using NolumiaScheduler.Domain.ValueObjects;
using NolumiaScheduler.Presentation.ViewModels;

namespace NolumiaSchedulerTest;

[TestClass]
public class EventEditInitializationTests
{
    [TestMethod]
    public void startDateとstartMinuteで新規初期値が設定される()
    {
        var vm = CreateViewModel();
        vm.InitializeNewEvent(new DateOnly(2026, 5, 6), 570);

        Assert.AreEqual(new DateTime(2026, 5, 6), vm.StartDate.Date);
        Assert.AreEqual(new TimeSpan(9, 30, 0), vm.StartTime);
        Assert.AreEqual(new TimeSpan(10, 30, 0), vm.EndTime);
    }

    [TestMethod]
    public void startMinuteは15分単位へ丸める()
    {
        var vm = CreateViewModel();
        vm.InitializeNewEvent(new DateOnly(2026, 5, 6), 575);
        Assert.AreEqual(new TimeSpan(9, 30, 0), vm.StartTime);
    }

    [TestMethod]
    public void 深夜帯でも終了時刻は日跨ぎしない()
    {
        var vm = CreateViewModel();
        vm.InitializeNewEvent(new DateOnly(2026, 5, 6), 1410);
        Assert.AreEqual(new TimeSpan(23, 30, 0), vm.StartTime);
        Assert.AreEqual(new TimeSpan(23, 59, 0), vm.EndTime);
    }


    [TestMethod]
    public void eventId指定時はOccurrenceKeyを保持する()
    {
        var vm = CreateViewModel(out var repo);
        var ev = CreateSingleEvent("e-load");
        repo.Save(ev);

        var key = new OccurrenceLocalKey(new LocalDateValue(2026, 5, 6), new LocalTimeValue(9, 30, 0));
        vm.LoadEvent("e-load", key);

        Assert.IsTrue(vm.IsEditing);
        Assert.IsTrue(vm.IsOccurrenceEditing);
        Assert.AreEqual(key, vm.EditingOccurrenceKey);
    }

    private static EventEditViewModel CreateViewModel()
    {
        var eventRepo = new InMemoryEventRepo();
        var calendarRepo = new InMemoryCalendarRepo();
        var service = new CalendarEventApplicationService(eventRepo);
        return new EventEditViewModel(service, eventRepo, calendarRepo);
    }

    private static EventEditViewModel CreateViewModel(out InMemoryEventRepo eventRepo)
    {
        eventRepo = new InMemoryEventRepo();
        var calendarRepo = new InMemoryCalendarRepo();
        var service = new CalendarEventApplicationService(eventRepo);
        return new EventEditViewModel(service, eventRepo, calendarRepo);
    }

    private static CalendarEvent CreateSingleEvent(string id)
    {
        var tz = new TimeZoneId("Asia/Tokyo");
        var now = DateTimeOffset.UtcNow;
        var start = new DateTimeOffset(2026,5,6,9,30,0,TimeSpan.FromHours(9));
        return CalendarEvent.CreateSingle(new EventId(id), new EventTitle("x"), null, NolumiaScheduler.Domain.ValueObjects.Visibility.Public, null, null, tz, false, new SingleEventSchedule(start, start.AddHours(1)), now);
    }

    private sealed class InMemoryEventRepo : ICalendarEventRepository
    {
        private readonly Dictionary<string, CalendarEvent> _map = [];
        public CalendarEvent? FindById(EventId id) => _map.TryGetValue(id.Value, out var ev) ? ev : null;
        public IReadOnlyList<CalendarEvent> FindAll() => _map.Values.ToList();
        public void Save(CalendarEvent ev) => _map[ev.Id.Value] = ev;
        public void Delete(EventId id) => _map.Remove(id.Value);
    }

    private sealed class InMemoryCalendarRepo : IBusinessCalendarRepository
    {
        public BusinessCalendar? FindById(BusinessCalendarId id) => null;
        public IReadOnlyList<BusinessCalendar> FindAll() => [];
        public void Save(BusinessCalendar calendar) { }
        public void Delete(BusinessCalendarId id) { }
    }
}
