using NolumiaScheduler.Application.Commands;
using NolumiaScheduler.Application.Services;
using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.Repositories;
using NolumiaScheduler.Domain.ValueObjects;

namespace NolumiaSchedulerTest;

/// <summary>
/// In-memory repository for testing BusinessCalendarApplicationService.
/// </summary>
internal sealed class InMemoryBusinessCalendarRepository : IBusinessCalendarRepository
{
    private readonly Dictionary<string, BusinessCalendar> _store = [];

    public BusinessCalendar? FindById(BusinessCalendarId id)
        => _store.TryGetValue(id.Value, out var cal) ? cal : null;

    public IReadOnlyList<BusinessCalendar> FindAll()
        => [.. _store.Values];

    public void Save(BusinessCalendar calendar)
        => _store[calendar.Id.Value] = calendar;

    public void Delete(BusinessCalendarId id)
        => _store.Remove(id.Value);
}

/// <summary>
/// Tests for holiday persistence through BusinessCalendarApplicationService.
/// </summary>
[TestClass]
public class BusinessCalendarServiceTests
{
    private static readonly TimeZoneId Tokyo = new("Asia/Tokyo");
    private static readonly LocalDateValue Jan1 = new(2026, 1, 1);
    private static readonly LocalDateValue May3 = new(2026, 5, 3);

    private InMemoryBusinessCalendarRepository _repo = null!;
    private BusinessCalendarApplicationService _service = null!;

    [TestInitialize]
    public void SetUp()
    {
        _repo = new InMemoryBusinessCalendarRepository();
        _service = new BusinessCalendarApplicationService(_repo);
    }

    // ── Create ────────────────────────────────────────────────

    [TestMethod]
    public void Create_WithNoHolidays_SavesEmptyHolidayList()
    {
        var created = _service.Create(new CreateBusinessCalendarCommand(
            "Test Cal", "Asia/Tokyo",
            [Weekday.Monday, Weekday.Friday]));

        var loaded = _repo.FindById(created.Id)!;
        Assert.IsEmpty(loaded.Holidays);
    }

    [TestMethod]
    public void AddHoliday_AfterCreate_PersistsHolidayWithName()
    {
        var created = _service.Create(new CreateBusinessCalendarCommand(
            "Test Cal", "Asia/Tokyo",
            [Weekday.Monday]));

        _service.AddHoliday(new AddHolidayCommand(created.Id.Value, Jan1, "元日"));

        var loaded = _repo.FindById(created.Id)!;
        Assert.HasCount(1, loaded.Holidays);
        Assert.AreEqual(Jan1, loaded.Holidays[0].Date);
        Assert.AreEqual("元日", loaded.Holidays[0].Name);
    }

    [TestMethod]
    public void AddHoliday_WithNullName_PersistsHolidayWithNullName()
    {
        var created = _service.Create(new CreateBusinessCalendarCommand(
            "Test Cal", "Asia/Tokyo", [Weekday.Monday]));

        _service.AddHoliday(new AddHolidayCommand(created.Id.Value, Jan1, null));

        var loaded = _repo.FindById(created.Id)!;
        Assert.HasCount(1, loaded.Holidays);
        Assert.IsNull(loaded.Holidays[0].Name);
    }

    [TestMethod]
    public void AddHoliday_MultipleDates_AllPersist()
    {
        var created = _service.Create(new CreateBusinessCalendarCommand(
            "Test Cal", "Asia/Tokyo", [Weekday.Monday]));

        _service.AddHoliday(new AddHolidayCommand(created.Id.Value, Jan1, "元日"));
        _service.AddHoliday(new AddHolidayCommand(created.Id.Value, May3, "憲法記念日"));

        var loaded = _repo.FindById(created.Id)!;
        Assert.HasCount(2, loaded.Holidays);
    }

    [TestMethod]
    public void AddHoliday_DuplicateDate_IsIgnoredByDomain()
    {
        var created = _service.Create(new CreateBusinessCalendarCommand(
            "Test Cal", "Asia/Tokyo", [Weekday.Monday]));

        _service.AddHoliday(new AddHolidayCommand(created.Id.Value, Jan1, "元日"));
        // Domain.AddHoliday ignores duplicates silently
        _service.AddHoliday(new AddHolidayCommand(created.Id.Value, Jan1, "別名"));

        var loaded = _repo.FindById(created.Id)!;
        Assert.HasCount(1, loaded.Holidays);
        // First name wins (domain silently ignores duplicate)
        Assert.AreEqual("元日", loaded.Holidays[0].Name);
    }

    // ── Update (Edit calendar) ────────────────────────────────

    [TestMethod]
    public void Update_PreservesExistingHolidays()
    {
        var created = _service.Create(new CreateBusinessCalendarCommand(
            "Original", "Asia/Tokyo", [Weekday.Monday]));
        _service.AddHoliday(new AddHolidayCommand(created.Id.Value, Jan1, "元日"));

        _service.Update(new UpdateBusinessCalendarCommand(
            created.Id.Value, "Renamed", [Weekday.Monday, Weekday.Friday]));

        var loaded = _repo.FindById(created.Id)!;
        Assert.AreEqual("Renamed", loaded.Name);
        Assert.HasCount(1, loaded.Holidays);
        Assert.AreEqual("元日", loaded.Holidays[0].Name);
    }

    [TestMethod]
    public void RemoveHoliday_RemovesCorrectDate()
    {
        var created = _service.Create(new CreateBusinessCalendarCommand(
            "Test Cal", "Asia/Tokyo", [Weekday.Monday]));
        _service.AddHoliday(new AddHolidayCommand(created.Id.Value, Jan1, "元日"));
        _service.AddHoliday(new AddHolidayCommand(created.Id.Value, May3, "憲法記念日"));

        _service.RemoveHoliday(new RemoveHolidayCommand(created.Id.Value, Jan1));

        var loaded = _repo.FindById(created.Id)!;
        Assert.HasCount(1, loaded.Holidays);
        Assert.AreEqual(May3, loaded.Holidays[0].Date);
    }

    // ── Save() logic in ViewModel (simulated directly) ────────

    [TestMethod]
    public void SaveViewModel_NewCalendar_HolidaysWithNameArePersisted()
    {
        // Simulate what BusinessCalendarEditViewModel.Save() does for new calendar
        var workdays = new List<Weekday> { Weekday.Monday };
        var created = _service.Create(new CreateBusinessCalendarCommand("New Cal", "Asia/Tokyo", workdays));

        // Simulate adding holidays in-memory before Save
        var inMemoryHolidays = new List<(LocalDateValue Date, string? Name)>
        {
            (Jan1, "元日"),
            (May3, null),
        };

        foreach (var (Date, Name) in inMemoryHolidays)
            _service.AddHoliday(new AddHolidayCommand(created.Id.Value, Date, Name));

        var loaded = _repo.FindById(created.Id)!;
        Assert.HasCount(2, loaded.Holidays);

        var jan = loaded.Holidays.First(h => h.Date.Equals(Jan1));
        Assert.AreEqual("元日", jan.Name);

        var may = loaded.Holidays.First(h => h.Date.Equals(May3));
        Assert.IsNull(may.Name);
    }

    [TestMethod]
    public void SaveViewModel_EditCalendar_AddNewHolidayIsPersisted()
    {
        // Start with a saved calendar with one holiday
        var created = _service.Create(new CreateBusinessCalendarCommand("Cal", "Asia/Tokyo", [Weekday.Monday]));
        _service.AddHoliday(new AddHolidayCommand(created.Id.Value, Jan1, "元日"));

        // Simulate ViewModel.Save() diff logic: add May3
        var calId = created.Id.Value;
        var existing = _repo.FindById(created.Id)!;
        var existingDates = existing.Holidays.Select(h => h.Date).ToHashSet();

        var desiredHolidays = new List<(LocalDateValue Date, string? Name)>
        {
            (Jan1, "元日"),
            (May3, "憲法記念日"),
        };
        var desiredDates = desiredHolidays.Select(h => h.Date).ToHashSet();

        foreach (var (Date, Name) in desiredHolidays)
            if (!existingDates.Contains(Date))
                _service.AddHoliday(new AddHolidayCommand(calId, Date, Name));

        foreach (var date in existingDates)
            if (!desiredDates.Contains(date))
                _service.RemoveHoliday(new RemoveHolidayCommand(calId, date));

        var loaded = _repo.FindById(created.Id)!;
        Assert.HasCount(2, loaded.Holidays);

        var may = loaded.Holidays.FirstOrDefault(h => h.Date.Equals(May3));
        Assert.IsNotNull(may);
        Assert.AreEqual("憲法記念日", may.Name);
    }

    [TestMethod]
    public void SaveViewModel_EditCalendar_NameChangeOnSameDateIsPersisted()
    {
        // After the fix: removing all then re-adding correctly updates names.
        var created = _service.Create(new CreateBusinessCalendarCommand("Cal", "Asia/Tokyo", [Weekday.Monday]));
        _service.AddHoliday(new AddHolidayCommand(created.Id.Value, Jan1, "元日"));

        var calId = created.Id.Value;

        // Fixed Save() logic: remove all, then re-add desired
        var existing = _repo.FindById(created.Id)!;
        var datesToRemove = existing.Holidays.Select(h => h.Date).ToList();
        foreach (var date in datesToRemove)
            _service.RemoveHoliday(new RemoveHolidayCommand(calId, date));

        var desiredHolidays = new List<(LocalDateValue Date, string? Name)>
        {
            (Jan1, "New Year"),  // same date, updated name
        };
        foreach (var (Date, Name) in desiredHolidays)
            _service.AddHoliday(new AddHolidayCommand(calId, Date, Name));

        var loaded = _repo.FindById(created.Id)!;
        Assert.HasCount(1, loaded.Holidays);
        Assert.AreEqual("New Year", loaded.Holidays[0].Name);
    }

    [TestMethod]
    public void SaveViewModel_EditCalendar_RemoveAndAddRoundTrip()
    {
        // Fixed logic: remove all then re-add → deletions and additions both work
        var created = _service.Create(new CreateBusinessCalendarCommand("Cal", "Asia/Tokyo", [Weekday.Monday]));
        _service.AddHoliday(new AddHolidayCommand(created.Id.Value, Jan1, "元日"));
        _service.AddHoliday(new AddHolidayCommand(created.Id.Value, May3, "憲法記念日"));

        var calId = created.Id.Value;
        var existing = _repo.FindById(created.Id)!;
        var datesToRemove = existing.Holidays.Select(h => h.Date).ToList();
        foreach (var date in datesToRemove)
            _service.RemoveHoliday(new RemoveHolidayCommand(calId, date));

        // Keep only May3, add a new date
        var newDate = new LocalDateValue(2026, 12, 31);
        var desiredHolidays = new List<(LocalDateValue Date, string? Name)>
        {
            (May3, "憲法記念日"),
            (newDate, "大晦日"),
        };
        foreach (var (Date, Name) in desiredHolidays)
            _service.AddHoliday(new AddHolidayCommand(calId, Date, Name));

        var loaded = _repo.FindById(created.Id)!;
        Assert.HasCount(2, loaded.Holidays);
        Assert.IsNull(loaded.Holidays.FirstOrDefault(h => h.Date.Equals(Jan1)));
        Assert.IsNotNull(loaded.Holidays.FirstOrDefault(h => h.Date.Equals(May3)));
        Assert.IsNotNull(loaded.Holidays.FirstOrDefault(h => h.Date.Equals(newDate)));
        Assert.AreEqual("大晦日", loaded.Holidays.First(h => h.Date.Equals(newDate)).Name);
    }
}
