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


    [TestMethod]
    public void 単発予定編集では範囲選択不要()
    {
        var vm = CreateViewModel(out var repo);
        repo.Save(CreateSingleEvent("single"));
        vm.LoadEvent("single");
        Assert.IsFalse(vm.RequiresRecurringEditScopeSelection);
    }

    [TestMethod]
    public void 繰り返し予定かつoccurrenceありは範囲選択が必要()
    {
        var vm = CreateViewModel(out var repo);
        repo.Save(CreateRecurringEvent("rec"));
        vm.LoadEvent("rec", new OccurrenceLocalKey(new LocalDateValue(2026,5,6), new LocalTimeValue(9,30,0)));
        Assert.IsTrue(vm.RequiresRecurringEditScopeSelection);
    }

    [TestMethod]
    public void これ以降は未実装エラーになる_旧ケース()
    {
        var vm = CreateViewModel(out var repo);
        repo.Save(CreateRecurringEvent("rec2"));
        vm.LoadEvent("rec2", new OccurrenceLocalKey(new LocalDateValue(2026,5,6), new LocalTimeValue(9,30,0)));
        vm.Save(RecurringEditScope.ThisAndFollowing);
        Assert.IsTrue(vm.HasValidationError);
    }


    [TestMethod]
    public void この予定のみで対象Occurrenceだけ変更される()
    {
        var vm = CreateViewModel(out var repo);
        repo.Save(CreateRecurringEvent("rec3"));

        var key = new OccurrenceLocalKey(new LocalDateValue(2026,5,6), new LocalTimeValue(9,30,0));
        vm.LoadEvent("rec3", key);
        vm.Title = "changed";
        vm.StartDate = new DateTime(2026,5,6);
        vm.StartTime = new TimeSpan(11,0,0);
        vm.EndTime = new TimeSpan(12,0,0);

        vm.Save(RecurringEditScope.ThisOccurrence);

        var saved = repo.FindById(new EventId("rec3"))!;
        Assert.IsFalse(vm.HasValidationError);
        Assert.IsGreaterThanOrEqualTo(saved.Exceptions.Count, 1);
        Assert.IsGreaterThanOrEqualTo(saved.Moves.Count, 0);
        Assert.AreEqual(RecurrenceType.Weekly, saved.RecurringSchedule!.RecurrenceRule.RuleType);
    }

    [TestMethod]
    public void これ以降は未実装エラーになる()
    {
        var vm = CreateViewModel(out var repo);
        repo.Save(CreateRecurringEvent("rec4"));
        vm.LoadEvent("rec4", new OccurrenceLocalKey(new LocalDateValue(2026,5,6), new LocalTimeValue(9,30,0)));
        vm.Save(RecurringEditScope.ThisAndFollowing);
        Assert.IsTrue(vm.HasValidationError);
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


    private static CalendarEvent CreateRecurringEvent(string id)
    {
        var tz = new TimeZoneId("Asia/Tokyo");
        var now = DateTimeOffset.UtcNow;
        return CalendarEvent.CreateRecurring(
            new EventId(id), new EventTitle("rec"), null, NolumiaScheduler.Domain.ValueObjects.Visibility.Public, null, null, tz, false,
            new RecurringEventSchedule(new LocalDateValue(2026, 5, 1), new LocalTimeValue(9, 30, 0), new LocalTimeValue(10, 30, 0),
                new RecurrenceRule(RecurrenceType.Weekly, 1, new LocalDateValue(2026, 12, 31), weekly: new WeeklyRule([Weekday.Wednesday])), false),
            now);
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
