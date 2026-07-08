using NolumiaScheduler.Application.Commands;
using NolumiaScheduler.Application.Services;
using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.Exceptions;
using NolumiaScheduler.Domain.Services;
using NolumiaScheduler.Domain.ValueObjects;
using Visibility = NolumiaScheduler.Domain.ValueObjects.Visibility;

namespace NolumiaScheduler.CoreTests;

/// <summary>
/// Behaviour matrix for recurring events. Each test loops over the recurrence types
/// (weekly / monthly / yearly) and exercises one axis of the matrix:
///   operation  : new / change / delete
///   scope      : this occurrence / this and following / entire series
///   edited part: start date (move) / end date / time only / content only
///   adjustment : none / next business day / previous business day
/// Results are verified by expanding the saved event into concrete occurrences.
/// </summary>
[TestClass]
public class RecurringMatrixTests
{
    private InMemoryCalendarEventRepository _repo = null!;
    private CalendarEventApplicationService _svc = null!;
    private OccurrenceExpander _expander = null!;

    [TestInitialize]
    public void Setup()
    {
        _repo = new InMemoryCalendarEventRepository();
        _svc = new CalendarEventApplicationService(
            _repo, _repo, new FakeClock(new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero)));
        _expander = new OccurrenceExpander(new BusinessDayShiftService());
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private string CreateSeries(RecurrenceType type, string title = "orig",
        AdjustmentRule? adjustment = null, LocalDateValue? end = null, bool allDay = false)
    {
        _svc.CreateRecurringEvent(new CreateRecurringEventCommand(
            Title: title,
            Location: null,
            Visibility: Visibility.Public,
            EventType: null,
            Description: null,
            TimeZone: "Asia/Tokyo",
            AllDay: allDay,
            StartDate: Rec.Start(type),
            StartTime: allDay ? null : Rec.StartTime,
            EndTime: allDay ? null : Rec.EndTime,
            RecurrenceRule: Rec.Rule(type, end, adjustment)));

        return _repo.FindAll().Single(e => e.Title.Value == title).Id.Value;
    }

    private CalendarEvent Ev(string id) =>
        _repo.FindById(new NolumiaScheduler.Domain.ValueObjects.EventId(id))!;

    private List<LocalDateValue> Dates(string id, LocalDateValue from, LocalDateValue to,
        BusinessCalendar? calendar = null) =>
        [.. _expander.Expand(Ev(id), from, to, calendar)
            .Select(o => o.Date)
            .OrderBy(d => d.ToDateOnly())];

    private List<EventOccurrence> Occurrences(string id, LocalDateValue from, LocalDateValue to,
        BusinessCalendar? calendar = null) =>
        [.. _expander.Expand(Ev(id), from, to, calendar)
            .OrderBy(o => o.Date.ToDateOnly())];

    // The expander keys occurrences by (candidate date, series start time), so target an
    // occurrence by its scheduled date and the series start time.
    private static OccurrenceLocalKey Key(RecurrenceType type, int occurrenceIndex)
        => new(Rec.Expected(type, occurrenceIndex + 1)[occurrenceIndex], Rec.StartTime);

    // ── NEW ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void New_各繰り返し種別で想定どおりの発生日になる()
    {
        foreach (var type in Rec.AllTypes)
        {
            Setup();
            var id = CreateSeries(type);
            var expected = Rec.Expected(type, 4);

            var dates = Dates(id, Rec.Start(type), expected[^1]);

            CollectionAssert.AreEqual(expected, dates, $"type={type}");
        }
    }

    // ── NEW + ADJUSTMENT (holiday shift none / forward / backward) ──────────

    [TestMethod]
    public void New_休日シフトで発生日が前後にずれる()
    {
        // holiday=2026-06-15 (a Mon / 15th / Jun-15 occurrence for all three types)
        var window = (from: new LocalDateValue(2026, 6, 8), to: new LocalDateValue(2026, 6, 22));

        foreach (var type in Rec.AllTypes)
        {
            // none: stays on the holiday
            Setup();
            var idNone = CreateSeries(type, adjustment: null);
            CollectionAssert.Contains(Dates(idNone, window.from, window.to, BusinessCal()),
                Rec.SharedHolidayDate, $"none type={type}");

            // forward: 06-15 -> 06-16 (next business day)
            Setup();
            var idFwd = CreateSeries(type, adjustment: new AdjustmentRule(AdjustmentDirection.Forward));
            var fwd = Dates(idFwd, window.from, window.to, BusinessCal());
            CollectionAssert.Contains(fwd, new LocalDateValue(2026, 6, 16), $"forward shifted type={type}");
            CollectionAssert.DoesNotContain(fwd, Rec.SharedHolidayDate, $"forward off-holiday type={type}");

            // backward: 06-15 -> 06-12 (previous business day)
            Setup();
            var idBack = CreateSeries(type, adjustment: new AdjustmentRule(AdjustmentDirection.Backward));
            var back = Dates(idBack, window.from, window.to, BusinessCal());
            CollectionAssert.Contains(back, new LocalDateValue(2026, 6, 12), $"backward shifted type={type}");
            CollectionAssert.DoesNotContain(back, Rec.SharedHolidayDate, $"backward off-holiday type={type}");
        }
    }

    private static BusinessCalendar BusinessCal() => new(
        new NolumiaScheduler.Domain.ValueObjects.BusinessCalendarId("cal"),
        "Cal",
        new TimeZoneId("Asia/Tokyo"),
        [Weekday.Monday, Weekday.Tuesday, Weekday.Wednesday, Weekday.Thursday, Weekday.Friday],
        [new Holiday(Rec.SharedHolidayDate, "test")]);

    // ── CHANGE · ENTIRE SERIES ──────────────────────────────────────────────

    [TestMethod]
    public void Change_系列全体_内容のみ_全発生のタイトルが変わり日付は不変()
    {
        foreach (var type in Rec.AllTypes)
        {
            Setup();
            var id = CreateSeries(type);
            var expected = Rec.Expected(type, 4);

            _svc.UpdateRecurringSeries(new UpdateRecurringSeriesCommand(
                id, "renamed", "Room X", Visibility.Public, false,
                Rec.StartTime, Rec.EndTime, Rec.Rule(type)));

            var occ = Occurrences(id, Rec.Start(type), expected[^1]);
            CollectionAssert.AreEqual(expected, occ.Select(o => o.Date).ToList(), $"dates type={type}");
            Assert.IsTrue(occ.All(o => o.Title.Value == "renamed"), $"title type={type}");
            Assert.IsTrue(occ.All(o => o.Location!.Value == "Room X"), $"location type={type}");
        }
    }

    [TestMethod]
    public void Change_系列全体_時間のみ_全発生の時刻が変わる()
    {
        foreach (var type in Rec.AllTypes)
        {
            Setup();
            var id = CreateSeries(type);
            var expected = Rec.Expected(type, 4);

            _svc.UpdateRecurringSeries(new UpdateRecurringSeriesCommand(
                id, "orig", null, Visibility.Public, false,
                new LocalTimeValue(13, 0, 0), new LocalTimeValue(14, 30, 0), Rec.Rule(type)));

            var occ = Occurrences(id, Rec.Start(type), expected[^1]);
            CollectionAssert.AreEqual(expected, occ.Select(o => o.Date).ToList(), $"dates type={type}");
            // 13:00–14:30 is now expressed as start 13:00 + 90-minute duration.
            Assert.IsTrue(occ.All(o => o.StartTime.Hour == 13 && o.DurationMinutes == 90), $"time type={type}");
        }
    }

    [TestMethod]
    public void Change_系列全体_時刻変更後もスキップが維持される()
    {
        foreach (var type in Rec.AllTypes)
        {
            Setup();
            var id = CreateSeries(type);
            var expected = Rec.Expected(type, 4);

            // Skip the 2nd occurrence before the series-wide time change.
            _svc.DeleteOccurrence(new SkipOccurrenceCommand(id, Key(type, 1)));

            // Series-wide time change (occurrence keys embed the start time).
            _svc.UpdateRecurringSeries(new UpdateRecurringSeriesCommand(
                id, "orig", null, Visibility.Public, false,
                new LocalTimeValue(13, 0, 0), new LocalTimeValue(14, 0, 0), Rec.Rule(type)));

            var occ = Occurrences(id, Rec.Start(type), expected[^1]);
            var dates = occ.Select(o => o.Date).ToList();

            // The skip is maintained after the series time changed (occurrence key is rekeyed).
            CollectionAssert.DoesNotContain(dates, expected[1], $"skip kept type={type}");
            Assert.IsTrue(occ.All(o => o.StartTime.Hour == 13), $"new time type={type}");
        }
    }

    [TestMethod]
    public void Change_系列全体_終了日短縮_発生数が減る()
    {
        foreach (var type in Rec.AllTypes)
        {
            Setup();
            var id = CreateSeries(type);
            var firstThree = Rec.Expected(type, 3);

            // End the series on its 3rd occurrence.
            _svc.UpdateRecurringSeries(new UpdateRecurringSeriesCommand(
                id, "orig", null, Visibility.Public, false,
                Rec.StartTime, Rec.EndTime, Rec.Rule(type, end: firstThree[^1])));

            // Expand far beyond the new end; only the first three may survive.
            var dates = Dates(id, Rec.Start(type), Rec.Expected(type, 10)[^1]);
            CollectionAssert.AreEqual(firstThree, dates, $"type={type}");
        }
    }

    // ── CHANGE · THIS AND FOLLOWING ──────────────────────────────────────────

    [TestMethod]
    public void Change_これ以降_3番目で分割し以降のみ変更される()
    {
        foreach (var type in Rec.AllTypes)
        {
            Setup();
            var id = CreateSeries(type);
            var expected = Rec.Expected(type, 5);
            var splitKey = Key(type, 2); // 3rd occurrence

            _svc.ChangeFollowingOccurrences(new ChangeFollowingOccurrencesCommand(
                EventId: id,
                FromOccurrenceKey: splitKey,
                NewTitle: "future",
                NewLocation: null,
                NewVisibility: Visibility.Public,
                NewAllDay: false,
                NewStartTime: new LocalTimeValue(15, 0, 0),
                NewEndTime: new LocalTimeValue(16, 0, 0),
                NewRecurrenceRule: Rec.Rule(type)));

            var all = _repo.FindAll();
            Assert.HasCount(2, all, $"split count type={type}");

            // Original keeps the first two occurrences only.
            var originalDates = Dates(id, Rec.Start(type), expected[^1]);
            CollectionAssert.AreEqual(Rec.Expected(type, 2), originalDates, $"original type={type}");

            // New series carries the remaining occurrences with the new content/time.
            var future = all.Single(e => e.Id.Value != id);
            var futureOcc = Occurrences(future.Id.Value, Rec.Start(type), expected[^1]);
            CollectionAssert.AreEqual(expected[2..], futureOcc.Select(o => o.Date).ToList(), $"future dates type={type}");
            Assert.IsTrue(futureOcc.All(o => o.Title.Value == "future"), $"future title type={type}");
            Assert.IsTrue(futureOcc.All(o => o.StartTime!.Hour == 15), $"future time type={type}");
        }
    }

    [TestMethod]
    public void Change_これ以降_先頭発生で分割すると元系列は消え新系列が全体を担う()
    {
        foreach (var type in Rec.AllTypes)
        {
            Setup();
            var id = CreateSeries(type);
            var expected = Rec.Expected(type, 4);
            var splitKey = Key(type, 0); // 1st occurrence == series start

            _svc.ChangeFollowingOccurrences(new ChangeFollowingOccurrencesCommand(
                EventId: id,
                FromOccurrenceKey: splitKey,
                NewTitle: "whole",
                NewLocation: null,
                NewVisibility: Visibility.Public,
                NewAllDay: false,
                NewStartTime: Rec.StartTime,
                NewEndTime: Rec.EndTime,
                NewRecurrenceRule: Rec.Rule(type)));

            var all = _repo.FindAll();
            Assert.HasCount(1, all, $"type={type}");
            Assert.IsNull(_repo.FindById(new NolumiaScheduler.Domain.ValueObjects.EventId(id)), $"original gone type={type}");

            var newId = all[0].Id.Value;
            CollectionAssert.AreEqual(expected, Dates(newId, Rec.Start(type), expected[^1]), $"dates type={type}");
            Assert.AreEqual("whole", all[0].Title.Value, $"title type={type}");
        }
    }

    // ── CHANGE · THIS OCCURRENCE ─────────────────────────────────────────────

    [TestMethod]
    public void Split_この予約のみ_対象発生が除外され単体予定が作成される()
    {
        foreach (var type in Rec.AllTypes)
        {
            Setup();
            var id = CreateSeries(type);
            var expected = Rec.Expected(type, 4);
            var targetKey = Key(type, 1); // 2nd occurrence
            var targetDate = expected[1];

            _svc.SplitThisOccurrence(new SplitThisOccurrenceCommand(
                EventId: id,
                OccurrenceKey: targetKey,
                Title: "special",
                Location: null,
                Visibility: Visibility.Public,
                AllDay: false,
                NewDate: targetDate,
                StartTime: Rec.StartTime,
                EndTime: Rec.EndTime));

            // The series now skips the 2nd occurrence
            var seriesDates = Dates(id, Rec.Start(type), expected[^1]);
            CollectionAssert.DoesNotContain(seriesDates, targetDate, $"occurrence removed from series type={type}");
            Assert.HasCount(3, seriesDates, $"series count type={type}");

            // A new standalone single event was created with the new content
            var all = _repo.FindAll();
            Assert.HasCount(2, all, $"total events type={type}");
            var single = all.Single(e => e.Id.Value != id && e.IsSingle());
            Assert.AreEqual("special", single.Title.Value, $"single title type={type}");
            Assert.AreEqual(targetDate, LocalSchedulePoint.LocalDateOf(
                single.SingleSchedule!.StartUtc, Rec.Start(type).ToDateOnly().DayOfWeek == DayOfWeek.Monday
                    ? new TimeZoneId("Asia/Tokyo").ToTimeZoneInfo()
                    : new TimeZoneId("Asia/Tokyo").ToTimeZoneInfo()), $"single date type={type}");
        }
    }

    [TestMethod]
    public void Split_この予約のみ_日付変更時は新しい単体予定が別日に作成される()
    {
        foreach (var type in Rec.AllTypes)
        {
            Setup();
            var id = CreateSeries(type);
            var expected = Rec.Expected(type, 4);
            var targetKey = Key(type, 1); // 2nd occurrence
            var movedDate = expected[1].AddDays(2);

            _svc.SplitThisOccurrence(new SplitThisOccurrenceCommand(
                EventId: id,
                OccurrenceKey: targetKey,
                Title: "orig",
                Location: null,
                Visibility: Visibility.Public,
                AllDay: false,
                NewDate: movedDate,
                StartTime: Rec.StartTime,
                EndTime: Rec.EndTime));

            // The original date is removed from the series
            var seriesDates = Dates(id, Rec.Start(type), expected[^1].AddDays(2));
            CollectionAssert.DoesNotContain(seriesDates, expected[1], $"old date removed type={type}");
            // Other occurrences remain in the series
            CollectionAssert.Contains(seriesDates, expected[0], $"first kept type={type}");
            CollectionAssert.Contains(seriesDates, expected[2], $"third kept type={type}");

            // The new single event is at the new date
            var all = _repo.FindAll();
            var single = all.Single(e => e.Id.Value != id && e.IsSingle());
            Assert.AreEqual(movedDate, LocalSchedulePoint.LocalDateOf(
                single.SingleSchedule!.StartUtc, new TimeZoneId("Asia/Tokyo").ToTimeZoneInfo()),
                $"single at new date type={type}");
        }
    }

    // ── DELETE ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void Delete_系列全体_予定が消える()
    {
        foreach (var type in Rec.AllTypes)
        {
            Setup();
            var id = CreateSeries(type);

            _svc.DeleteEvent(id);

            Assert.IsNull(_repo.FindById(new NolumiaScheduler.Domain.ValueObjects.EventId(id)), $"type={type}");
            Assert.HasCount(0, _repo.FindAll(), $"type={type}");
        }
    }

    [TestMethod]
    public void Delete_この予定のみ_対象発生だけ除外される()
    {
        foreach (var type in Rec.AllTypes)
        {
            Setup();
            var id = CreateSeries(type);
            var expected = Rec.Expected(type, 4);
            var targetKey = Key(type, 1); // 2nd occurrence

            _svc.DeleteOccurrence(new SkipOccurrenceCommand(id, targetKey));

            var dates = Dates(id, Rec.Start(type), expected[^1]);
            CollectionAssert.DoesNotContain(dates, expected[1], $"skipped removed type={type}");
            CollectionAssert.Contains(dates, expected[0], $"first kept type={type}");
            CollectionAssert.Contains(dates, expected[2], $"third kept type={type}");
            Assert.HasCount(3, dates, $"count type={type}");
        }
    }

    // ── END-DATE VALIDATION (domain guard) ───────────────────────────────────

    [TestMethod]
    public void EndDate_開始日より前の終了日で新規作成すると例外()
    {
        foreach (var type in Rec.AllTypes)
        {
            Setup();
            var start = Rec.Start(type);
            var badEnd = start.AddDays(-1);

            Assert.ThrowsExactly<DomainException>(() => _svc.CreateRecurringEvent(
                new CreateRecurringEventCommand(
                    "x", null, Visibility.Public, null, null, "Asia/Tokyo", false,
                    start, Rec.StartTime, Rec.EndTime, Rec.Rule(type, end: badEnd))),
                $"type={type}");
        }
    }

    [TestMethod]
    public void EndDate_系列全体更新で開始日より前の終了日は例外()
    {
        foreach (var type in Rec.AllTypes)
        {
            Setup();
            var id = CreateSeries(type);
            var badEnd = Rec.Start(type).AddDays(-1);

            Assert.ThrowsExactly<DomainException>(() => _svc.UpdateRecurringSeries(
                new UpdateRecurringSeriesCommand(
                    id, "orig", null, Visibility.Public, false,
                    Rec.StartTime, Rec.EndTime, Rec.Rule(type, end: badEnd))),
                $"type={type}");
        }
    }
}
