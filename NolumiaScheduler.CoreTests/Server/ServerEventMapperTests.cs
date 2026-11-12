using NolumiaScheduler.Domain.Entities;
using NolumiaScheduler.Domain.ValueObjects;
using NolumiaScheduler.Infrastructure.Server;

namespace NolumiaScheduler.CoreTests.Server;

[TestClass]
public class ServerEventMapperTests
{
    private static readonly BusinessCalendarId DaysOff = new(ServerDaysOffCalendar.Id);

    [TestMethod]
    public void ToDomain_ServerIntegerIdBecomesStringEventId()
    {
        var ev = ServerEventMapper.ToDomain(ServerEvents.Single(42), DaysOff);

        Assert.AreEqual("42", ev.Id.Value);
        Assert.IsTrue(ev.IsSingle());
        Assert.AreEqual("Meeting", ev.Title.Value);
        Assert.AreEqual("Room A", ev.Location!.Value);
        Assert.AreEqual(60, ev.SingleSchedule!.DurationMinutes);
        Assert.AreEqual(new DateTimeOffset(2026, 11, 2, 1, 0, 0, TimeSpan.Zero), ev.SingleSchedule.StartUtc);
        Assert.AreEqual(new EventAlarm(true, true, true, true, true), ev.Alarm);
        Assert.IsFalse(ev.IsReadOnly);
    }

    [TestMethod]
    public void ToDomain_ReadsRecurrenceRuleExceptionsAndMoves_BusinessDayShiftPointsToDaysOffLayer()
    {
        var dto = ServerEvents.Weekly(7) with
        {
            ColorKey = "TOMATO",
            Exceptions = [new EventExceptionDto(new OccurrenceKeyDto("2026-11-04", "09:00"), "SKIP")],
            Moves = [new EventMoveDto(new OccurrenceKeyDto("2026-11-09", "09:00"), "2026-11-10", "13:30", 30, "Moved", null)],
        };

        var ev = ServerEventMapper.ToDomain(dto, DaysOff);

        var rule = ev.RecurringSchedule!.RecurrenceRule;
        Assert.AreEqual(RecurrenceType.Weekly, rule.RuleType);
        CollectionAssert.AreEqual(new[] { Weekday.Monday, Weekday.Wednesday }, rule.Weekly!.Weekdays.ToArray());
        Assert.AreEqual(ServerEventMapper.NoEndDate, rule.EndDate, "A null end date means \"no end\"");
        Assert.AreEqual(ServerDaysOffCalendar.Id, rule.Adjustment!.CalendarId!.Value);
        Assert.AreEqual(1, rule.Adjustment.ShiftAmount);
        Assert.AreEqual(EventColorKey.Tomato, ev.ColorKey);

        Assert.AreEqual(ExceptionType.Skip, ev.Exceptions.Single().Type);
        Assert.AreEqual(new OccurrenceLocalKey(new LocalDateValue(2026, 11, 4), new LocalTimeValue(9, 0)), ev.Exceptions[0].OccurrenceKey);

        var move = ev.Moves.Single();
        Assert.AreEqual(new LocalDateValue(2026, 11, 10), move.NewDate);
        Assert.AreEqual(new LocalTimeValue(13, 30), move.NewStartTime);
        Assert.AreEqual(30, move.NewDurationMinutes);
        Assert.AreEqual("Moved", move.Title!.Value);
    }

    [TestMethod]
    public void ToRuleDto_ReadRuleCanBeWrittenBackAsIs()
    {
        var dto = ServerEvents.Weekly(7).Recurrence! with
        {
            EndDate = "2027-03-31",
            Adjustment = new AdjustmentRuleDto("ALWAYS", "CALENDAR_DAY", -2, "CANCEL"),
        };

        var back = ServerEventMapper.ToRuleDto(ServerEventMapper.ToRule(dto, DaysOff));

        Assert.AreEqual(FakeSchedulerServer.Serialize(dto), FakeSchedulerServer.Serialize(back));
    }

    [TestMethod]
    public void ToRuleDto_MonthlyAndYearlyRules()
    {
        var monthlyNth = new RecurrenceRuleDto("MONTHLY", 2, null, null, new MonthlyRuleDto("NTH_WEEKDAY", WeekIndex: -1, Weekday: "FR"), null, null);
        var monthlyLast = new RecurrenceRuleDto("MONTHLY", 1, null, null, new MonthlyRuleDto("LAST_DAY"), null, null);
        var yearly = new RecurrenceRuleDto("YEARLY", 1, null, null, null, new YearlyRuleDto("DAY_OF_MONTH", 6, Day: 15), null);

        foreach (var dto in new[] { monthlyNth, monthlyLast, yearly })
        {
            var back = ServerEventMapper.ToRuleDto(ServerEventMapper.ToRule(dto, DaysOff));
            Assert.AreEqual(FakeSchedulerServer.Serialize(dto), FakeSchedulerServer.Serialize(back));
        }
    }

    [TestMethod]
    public void ToRuleDto_WeekdaysOrderedFromMonday()
    {
        var rule = new RecurrenceRule(RecurrenceType.Weekly, 1, ServerEventMapper.NoEndDate,
            weekly: new WeeklyRule([Weekday.Sunday, Weekday.Friday, Weekday.Monday]));

        CollectionAssert.AreEqual(new[] { "MO", "FR", "SU" }, ServerEventMapper.ToRuleDto(rule).Weekly!.Weekdays.ToArray());
    }

    [TestMethod]
    public void NotificationFields_ReadAndWrittenWithServerNames()
    {
        var json = FakeSchedulerServer.Serialize(new AlarmDto(true, true, false, true, false));

        Assert.AreEqual("""{"enabled":true,"notify_15_min":true,"notify_5_min":false,"notify_1_min":true,"notify_at_start":false}""", json);
        Assert.AreEqual(new AlarmDto(true, true, false, true, false),
            System.Text.Json.JsonSerializer.Deserialize<AlarmDto>(json, ServerJson.Options));
    }

    [TestMethod]
    public void ToUpdateDto_SendsBackServerValuesForFieldsUnknownToApp()
    {
        var server = ServerEvents.Single(42);
        var ev = ServerEventMapper.ToDomain(server, DaysOff);

        var body = ServerEventMapper.ToUpdateDto(ev, server);

        Assert.AreEqual(77L, body.TaskId);
        Assert.AreEqual("TASK", body.EventType);
        Assert.AreEqual(5L, body.CalendarId);
        Assert.AreEqual(3, body.ExpectedVersion);
    }

    [TestMethod]
    public void ToDomain_ImportedOccurrenceIsReadOnlyWithReminder_IdStableAcrossReloads()
    {
        var dto = new ImportedOccurrenceDto(
            Id: "imported:9:0", CalendarId: 9, Title: "Review", Location: "Teams",
            Start: new DateTimeOffset(2026, 11, 5, 5, 0, 0, TimeSpan.Zero), DurationMinutes: 30,
            IsAllDay: false, IsPrivate: true, Reminder: new AlarmDto(true, true, true, true, true));

        var ev = ServerEventMapper.ToDomain(dto, "Asia/Tokyo");
        var again = ServerEventMapper.ToDomain(dto with { Id = "imported:9:17" }, "Asia/Tokyo");

        Assert.IsTrue(ev.IsReadOnly);
        Assert.AreEqual(ServerEventMapper.ImportedSourceId, ev.ExternalOrigin!.SourceId);
        Assert.AreEqual(new EventAlarm(true, true, true, true, true), ev.Alarm);
        Assert.AreEqual(Visibility.Private, ev.Visibility);
        Assert.AreEqual(ev.Id.Value, again.Id.Value, "The same occurrence keeps the same id even if numbers in the response change");
    }

    [TestMethod]
    public void ToDomain_ImportedOccurrenceWithoutReminder_DoesNotRing()
    {
        var dto = new ImportedOccurrenceDto("x", 9, "Holiday", null,
            new DateTimeOffset(2026, 11, 4, 15, 0, 0, TimeSpan.Zero), 1440, IsAllDay: true);

        var ev = ServerEventMapper.ToDomain(dto, "Asia/Tokyo");

        Assert.IsFalse(ev.Alarm!.IsEnabled);
    }
}
