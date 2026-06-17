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
        Assert.AreEqual(13, future.RecurringSchedule!.StartTime.Hour);
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

    [TestMethod]
    public void 複数曜日の週次予定を読み込むと該当曜日がすべて選択される()
    {
        var vm = CreateViewModel(out var repo);
        repo.Save(CreateWeeklyEvent("rec-multi", Weekday.Tuesday, Weekday.Thursday));

        vm.LoadEvent("rec-multi", new OccurrenceLocalKey(new LocalDateValue(2026, 5, 5), new LocalTimeValue(9, 30, 0)));

        Assert.IsTrue(vm.WeekTue, "Tuesday should be checked");
        Assert.IsTrue(vm.WeekThu, "Thursday should be checked");
        Assert.IsFalse(vm.WeekMon, "Monday should not be checked");
        Assert.IsFalse(vm.WeekWed, "Wednesday should not be checked");
    }

    [TestMethod]
    public void 系列全体で終了日を開始日より前にすると検証エラーになる()
    {
        var vm = CreateViewModel(out var repo);
        repo.Save(CreateRecurringEvent("rec-end")); // series starts 2026-05-01

        vm.LoadEvent("rec-end", new OccurrenceLocalKey(new LocalDateValue(2026, 5, 6), new LocalTimeValue(9, 30, 0)));
        vm.HasEndDate = true;
        vm.EndDate = new DateTime(2026, 4, 1); // before the series start

        vm.Save(RecurringEditScope.EntireSeries);

        Assert.IsTrue(vm.HasValidationError);
        Assert.HasCount(1, repo.FindAll());
    }

    [TestMethod]
    public void 新規週次予定で終了日が開始日より前だと検証エラーになる()
    {
        var vm = CreateViewModel();
        vm.InitializeNewEvent(new DateOnly(2026, 6, 1), 540);
        vm.RepeatTypeIndex = (int)RepeatTypeIndex.Weekly; // Monday selected by default
        vm.HasEndDate = true;
        vm.EndDate = new DateTime(2026, 5, 1); // before start

        vm.Save();

        Assert.IsTrue(vm.HasValidationError);
    }

    [TestMethod]
    public void 読み込み時に過去終了日が既定値で上書きされない()
    {
        var vm = CreateViewModel(out var repo);
        // A series that already ended in the past.
        repo.Save(CreateWeeklyEvent("rec-ended", new LocalDateValue(2020, 1, 6),
            new LocalDateValue(2020, 3, 30), Weekday.Monday));

        vm.LoadEvent("rec-ended", new OccurrenceLocalKey(new LocalDateValue(2020, 1, 6), new LocalTimeValue(9, 30, 0)));

        Assert.IsTrue(vm.HasEndDate);
        Assert.AreEqual(new DateTime(2020, 3, 30), vm.EndDate.Date);
    }

    [TestMethod]
    public void 新規週次は開始日の曜日が初期選択される()
    {
        var vm = CreateViewModel();
        vm.InitializeNewEvent(new DateOnly(2026, 6, 3), 540); // Wednesday
        vm.RepeatTypeIndex = (int)RepeatTypeIndex.Weekly;

        Assert.IsTrue(vm.WeekWed, "Wednesday should be the default");
        Assert.IsFalse(vm.WeekMon);
        Assert.IsFalse(vm.WeekSun);
        Assert.IsFalse(vm.WeekTue);
        Assert.IsFalse(vm.WeekThu);
    }

    [TestMethod]
    public void 新規月次は開始日の日が初期値になる()
    {
        var vm = CreateViewModel();
        vm.InitializeNewEvent(new DateOnly(2026, 6, 18), 540);
        vm.RepeatTypeIndex = (int)RepeatTypeIndex.Monthly;

        Assert.AreEqual(18, vm.DayOfMonth);
    }

    [TestMethod]
    public void 新規年次は開始日の月日が初期値になる()
    {
        var vm = CreateViewModel();
        vm.InitializeNewEvent(new DateOnly(2026, 6, 15), 540);
        vm.RepeatTypeIndex = (int)RepeatTypeIndex.Yearly;

        Assert.AreEqual(6, vm.YearlyMonth);
        Assert.AreEqual(15, vm.YearlyDay);
    }

    [TestMethod]
    public void 既存週次の読み込みは開始日デフォルトで上書きされない()
    {
        // Series starts 2026-05-01 (a Friday) but recurs Tue + Thu. Loading must keep Tue/Thu
        // and must not force-select the start weekday (Friday).
        var vm = CreateViewModel(out var repo);
        repo.Save(CreateWeeklyEvent("rec-load-def", Weekday.Tuesday, Weekday.Thursday));
        vm.LoadEvent("rec-load-def", new OccurrenceLocalKey(new LocalDateValue(2026, 5, 5), new LocalTimeValue(9, 30, 0)));

        Assert.IsTrue(vm.WeekTue);
        Assert.IsTrue(vm.WeekThu);
        Assert.IsFalse(vm.WeekFri);
    }

    private static EventEditViewModel CreateViewModel()
    {
        var eventRepo = new InMemoryEventRepo();
        var calendarRepo = new InMemoryCalendarRepo();
        var eventService = new CalendarEventApplicationService(eventRepo, eventRepo, TimeProvider.System);
        var calendarService = new BusinessCalendarApplicationService(calendarRepo);
        return new EventEditViewModel(eventService, calendarService, TimeProvider.System);
    }

    private static EventEditViewModel CreateViewModel(out InMemoryEventRepo eventRepo)
    {
        eventRepo = new InMemoryEventRepo();
        var calendarRepo = new InMemoryCalendarRepo();
        var eventService = new CalendarEventApplicationService(eventRepo, eventRepo, TimeProvider.System);
        var calendarService = new BusinessCalendarApplicationService(calendarRepo);
        return new EventEditViewModel(eventService, calendarService, TimeProvider.System);
    }

    private static CalendarEvent CreateSingleEvent(string id)
    {
        var tz = new TimeZoneId("Asia/Tokyo");
        var now = DateTimeOffset.UtcNow;
        var start = new DateTimeOffset(2026,5,6,9,30,0,TimeSpan.FromHours(9));
        return CalendarEvent.CreateSingle(new EventId(id), new EventTitle("x"), null, NolumiaScheduler.Domain.ValueObjects.Visibility.Public, null, null, tz, new SingleEventSchedule(new LocalDateValue(2026, 5, 6), new LocalTimeValue(9, 30, 0), 60), now);
    }


    private static CalendarEvent CreateRecurringEvent(string id)
    {
        var tz = new TimeZoneId("Asia/Tokyo");
        var now = DateTimeOffset.UtcNow;
        return CalendarEvent.CreateRecurring(
            new EventId(id), new EventTitle("rec"), null, NolumiaScheduler.Domain.ValueObjects.Visibility.Public, null, null, tz,
            new RecurringEventSchedule(new LocalDateValue(2026, 5, 1), new LocalTimeValue(9, 30, 0), 60,
                new RecurrenceRule(RecurrenceType.Weekly, 1, new LocalDateValue(2026, 12, 31), weekly: new WeeklyRule([Weekday.Wednesday]))),
            now);
    }

    private static CalendarEvent CreateWeeklyEvent(string id, params Weekday[] days)
        => CreateWeeklyEvent(id, new LocalDateValue(2026, 5, 1), new LocalDateValue(2026, 12, 31), days);

    private static CalendarEvent CreateWeeklyEvent(string id, LocalDateValue start, LocalDateValue end, params Weekday[] days)
    {
        var tz = new TimeZoneId("Asia/Tokyo");
        var now = DateTimeOffset.UtcNow;
        return CalendarEvent.CreateRecurring(
            new EventId(id), new EventTitle("rec"), null, NolumiaScheduler.Domain.ValueObjects.Visibility.Public, null, null, tz,
            new RecurringEventSchedule(start, new LocalTimeValue(9, 30, 0), 60,
                new RecurrenceRule(RecurrenceType.Weekly, 1, end, weekly: new WeeklyRule([.. days]))),
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
