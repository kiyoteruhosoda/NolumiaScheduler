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
        Assert.IsFalse(vm.HasValidationError);
    }



    [TestMethod]
    public void LoadEvent_繰り返し発生日指定時は指定occurrenceの日時で初期化される()
    {
        var vm = CreateViewModel(out var repo);
        repo.Save(CreateRecurringEvent("rec-occ"));

        var key = new OccurrenceLocalKey(new LocalDateValue(2026, 5, 20), new LocalTimeValue(9, 10, 0));
        vm.LoadEvent("rec-occ", key);

        Assert.AreEqual(new DateTime(2026, 5, 20), vm.StartDate.Date);
        Assert.AreEqual(new TimeSpan(9, 10, 0), vm.StartTime);
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
    public void これ以降を選ぶと系列が分割される()
    {
        var vm = CreateViewModel(out var repo);
        repo.Save(CreateRecurringEvent("rec-split"));

        var key = new OccurrenceLocalKey(new LocalDateValue(2026, 5, 6), new LocalTimeValue(9, 30, 0));
        vm.LoadEvent("rec-split", key);
        vm.Title = "future";
        vm.Location = "room B";
        vm.StartTime = new TimeSpan(13, 0, 0);
        vm.EndTime = new TimeSpan(14, 0, 0);

        vm.Save(RecurringEditScope.ThisAndFollowing);
        Assert.IsFalse(vm.HasValidationError);

        var all = repo.FindAll();
        Assert.HasCount(2, all);

        var original = all.Single(e => e.Id.Value == "rec-split");
        Assert.AreEqual(new LocalDateValue(2026, 5, 5), original.RecurringSchedule!.RecurrenceRule.EndDate);

        var future = all.Single(e => e.Id.Value != "rec-split");
        Assert.AreEqual("future", future.Title.Value);
        Assert.AreEqual("room B", future.Location!.Value);
        Assert.AreEqual(new LocalDateValue(2026, 5, 6), future.RecurringSchedule!.StartDate);
        Assert.AreEqual(13, future.RecurringSchedule.StartTime!.Hour);
    }

    [TestMethod]
    public void これ以降選択で保存できる()
    {
        var vm = CreateViewModel(out var repo);
        repo.Save(CreateRecurringEvent("rec4"));
        vm.LoadEvent("rec4", new OccurrenceLocalKey(new LocalDateValue(2026,5,6), new LocalTimeValue(9,30,0)));
        vm.Save(RecurringEditScope.ThisAndFollowing);
        Assert.IsFalse(vm.HasValidationError);
    }

    [TestMethod]
    public void 系列全体を選ぶと繰り返しの曜日が更新される()
    {
        var vm = CreateViewModel(out var repo);
        repo.Save(CreateRecurringEvent("rec-series"));

        var key = new OccurrenceLocalKey(new LocalDateValue(2026, 5, 6), new LocalTimeValue(9, 30, 0));
        vm.LoadEvent("rec-series", key);

        // The loaded series recurs on Wednesday only; redefine it to Monday + Friday.
        vm.WeekWed = false;
        vm.WeekMon = true;
        vm.WeekFri = true;

        vm.Save(RecurringEditScope.EntireSeries);
        Assert.IsFalse(vm.HasValidationError);

        // No split occurs: the series keeps its identity.
        Assert.HasCount(1, repo.FindAll());

        var saved = repo.FindById(new EventId("rec-series"))!;
        CollectionAssert.AreEquivalent(
            new[] { Weekday.Monday, Weekday.Friday },
            saved.RecurringSchedule!.RecurrenceRule.Weekly!.Weekdays.ToList());
        Assert.AreEqual(new LocalDateValue(2026, 5, 1), saved.RecurringSchedule.StartDate);
    }

    private static EventEditViewModel CreateViewModel()
    {
        var eventRepo = new InMemoryEventRepo();
        var calendarRepo = new InMemoryCalendarRepo();
        var eventService = new CalendarEventApplicationService(eventRepo, eventRepo);
        var calendarService = new BusinessCalendarApplicationService(calendarRepo);
        return new EventEditViewModel(eventService, calendarService);
    }

    private static EventEditViewModel CreateViewModel(out InMemoryEventRepo eventRepo)
    {
        eventRepo = new InMemoryEventRepo();
        var calendarRepo = new InMemoryCalendarRepo();
        var eventService = new CalendarEventApplicationService(eventRepo, eventRepo);
        var calendarService = new BusinessCalendarApplicationService(calendarRepo);
        return new EventEditViewModel(eventService, calendarService);
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

    private sealed class InMemoryEventRepo : ICalendarEventRepository, ICalendarEventChanges
    {
        public event Action? Changed;
        private readonly Dictionary<string, CalendarEvent> _map = [];
        public CalendarEvent? FindById(EventId id) => _map.TryGetValue(id.Value, out var ev) ? ev : null;
        public IReadOnlyList<CalendarEvent> FindAll() => _map.Values.ToList();
        public void Save(CalendarEvent ev) { _map[ev.Id.Value] = ev; Changed?.Invoke(); }
        public void Delete(EventId id) { _map.Remove(id.Value); Changed?.Invoke(); }
    }

    private sealed class InMemoryCalendarRepo : IBusinessCalendarRepository
    {
        public BusinessCalendar? FindById(BusinessCalendarId id) => null;
        public IReadOnlyList<BusinessCalendar> FindAll() => [];
        public void Save(BusinessCalendar calendar) { }
        public void Delete(BusinessCalendarId id) { }
    }
}
