using System.Globalization;
using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.Entities;
using NolumiaScheduler.Domain.ValueObjects;
using Location = NolumiaScheduler.Domain.ValueObjects.Location;

namespace NolumiaScheduler.Infrastructure.Server;

/// <summary>
/// Translates between the server's event shape and the app's <see cref="CalendarEvent"/>.
/// Identifiers: the server's integer id becomes the <see cref="EventId"/> string (decision 7).
/// Fields the app does not know (calendar, category, linked task) are not on the aggregate; the
/// write side copies them back from the server copy so editing in the app never drops them.
/// Business-day adjustments point at <paramref name="daysOffCalendarId"/>, the calendar built
/// from the server's day-off layers (<see cref="ServerDaysOffCalendar"/>).
/// </summary>
public static class ServerEventMapper
{
    /// <summary>The app's "no end date" (the server sends null).</summary>
    public static readonly LocalDateValue NoEndDate = new(9999, 12, 31);

    // ── Server → app ─────────────────────────────────────────────────────

    public static CalendarEvent ToDomain(EventDto dto, BusinessCalendarId daysOffCalendarId)
    {
        var isRecurring = dto.Kind == "RECURRING";
        return CalendarEvent.Reconstitute(
            id: new EventId(dto.Id.ToString(CultureInfo.InvariantCulture)),
            kind: isRecurring ? EventKind.Recurring : EventKind.Single,
            title: new EventTitle(dto.Title),
            location: string.IsNullOrEmpty(dto.Location) ? null : new Location(dto.Location),
            visibility: Visibility.Public,
            eventType: null,
            description: string.IsNullOrEmpty(dto.Description) ? null : new Description(dto.Description),
            timeZoneId: new TimeZoneId(dto.TimeZone),
            singleSchedule: isRecurring ? null : new SingleEventSchedule(dto.Start, dto.DurationMinutes),
            recurringSchedule: isRecurring
                ? new RecurringEventSchedule(dto.Start, dto.DurationMinutes,
                    ToRule(dto.Recurrence ?? throw new FormatException($"Recurring event {dto.Id} has no rule."), daysOffCalendarId))
                : null,
            createdAt: dto.CreatedAt ?? DateTimeOffset.UnixEpoch,
            updatedAt: dto.UpdatedAt ?? dto.CreatedAt ?? DateTimeOffset.UnixEpoch,
            exceptions: [.. dto.Exceptions.Select(ToException)],
            moves: [.. dto.Moves.Select(ToMove)],
            version: new VersionNo(Math.Max(0, dto.Version)),
            alarm: ToAlarm(dto.Alarm),
            colorKey: ParseColor(dto.ColorKey));
    }

    /// <summary>Source id on events built from the server's imported occurrences (Outlook, iCalendar).</summary>
    public const string ImportedSourceId = "server-imported";

    /// <summary>
    /// An imported occurrence (read-only) as a single event, so the calendar shows it and the alarm
    /// service rings its reminder like any other event. The server's occurrence id is only unique within
    /// one response, so the event id is built from what identifies the occurrence instead (calendar,
    /// start, title) and stays the same across refreshes.
    /// </summary>
    public static CalendarEvent ToDomain(ImportedOccurrenceDto dto, string timeZoneId)
    {
        var key = string.Create(CultureInfo.InvariantCulture,
            $"imported:{dto.CalendarId}:{dto.Start.UtcDateTime:yyyyMMdd'T'HHmm}:{StableHash(dto.Title):x8}");
        var title = string.IsNullOrWhiteSpace(dto.Title) ? "(no title)" : dto.Title;
        return CalendarEvent.CreateExternalSingle(
            new EventId(key),
            new ExternalOrigin(ImportedSourceId, key, null),
            new EventTitle(title),
            string.IsNullOrWhiteSpace(dto.Location) ? null : new Location(dto.Location),
            dto.IsPrivate ? Visibility.Private : Visibility.Public,
            new TimeZoneId(timeZoneId),
            new SingleEventSchedule(dto.Start, Math.Max(1, dto.DurationMinutes)),
            // Same "no alarm" shape the old local Outlook import used for appointments without a reminder.
            ToAlarm(dto.Reminder) ?? new EventAlarm(false, false, false, false, false),
            DateTimeOffset.UnixEpoch);
    }

    // FNV-1a: deterministic across runs (string.GetHashCode is randomized per process).
    private static uint StableHash(string text)
    {
        var hash = 2166136261u;
        foreach (var c in text)
        {
            hash ^= c;
            hash *= 16777619u;
        }
        return hash;
    }

    public static RecurrenceRule ToRule(RecurrenceRuleDto dto, BusinessCalendarId? daysOffCalendarId)
    {
        var type = dto.Type switch
        {
            "WEEKLY" => RecurrenceType.Weekly,
            "MONTHLY" => RecurrenceType.Monthly,
            "YEARLY" => RecurrenceType.Yearly,
            _ => throw new FormatException($"Unknown recurrence type: {dto.Type}"),
        };
        return new RecurrenceRule(
            type,
            dto.Interval,
            dto.EndDate is null ? NoEndDate : ParseDate(dto.EndDate),
            weekly: dto.Weekly is null ? null : new WeeklyRule([.. dto.Weekly.Weekdays.Select(ParseWeekday)]),
            monthly: dto.Monthly is null ? null : dto.Monthly.Kind switch
            {
                "DAY_OF_MONTH" => new DayOfMonthMonthlyRule(dto.Monthly.Day ?? 1),
                "NTH_WEEKDAY" => new NthWeekdayMonthlyRule(dto.Monthly.WeekIndex ?? 1, ParseWeekday(dto.Monthly.Weekday ?? "MO")),
                "LAST_DAY" => new LastDayOfMonthMonthlyRule(),
                _ => throw new FormatException($"Unknown monthly kind: {dto.Monthly.Kind}"),
            },
            yearly: dto.Yearly is null ? null : dto.Yearly.Kind switch
            {
                "DAY_OF_MONTH" => new DayOfMonthYearlyRule(dto.Yearly.Month, dto.Yearly.Day ?? 1),
                "NTH_WEEKDAY" => new NthWeekdayYearlyRule(dto.Yearly.Month, dto.Yearly.WeekIndex ?? 1, ParseWeekday(dto.Yearly.Weekday ?? "MO")),
                _ => throw new FormatException($"Unknown yearly kind: {dto.Yearly.Kind}"),
            },
            adjustment: dto.Adjustment is null ? null : new AdjustmentRule(
                dto.Adjustment.Condition == "ALWAYS" ? AdjustmentCondition.Always : AdjustmentCondition.Holiday,
                dto.Adjustment.ShiftUnit == "CALENDAR_DAY" ? AdjustmentShiftUnit.CalendarDay : AdjustmentShiftUnit.BusinessDay,
                dto.Adjustment.ShiftAmount,
                daysOffCalendarId,
                dto.Adjustment.Action == "CANCEL" ? AdjustmentAction.Cancel : AdjustmentAction.Shift));
    }

    public static EventAlarm? ToAlarm(AlarmDto? dto)
        => dto is null ? null : new EventAlarm(dto.Enabled, dto.Notify15Min, dto.Notify5Min, dto.Notify1Min, dto.NotifyAtStart);

    private static EventException ToException(EventExceptionDto dto)
    {
        var key = ToKey(dto.Occurrence);
        // The server only creates skips; OVERRIDE rows are legacy and carry no payload over the wire.
        return dto.Type == "SKIP"
            ? EventException.CreateSkip(key)
            : EventException.CreateOverride(key, new ExceptionOverride());
    }

    private static EventMove ToMove(EventMoveDto dto) => new(
        ToKey(dto.Occurrence),
        ParseDate(dto.NewDate),
        dto.NewStartTime is null ? null : ParseTime(dto.NewStartTime),
        dto.NewDurationMinutes,
        dto.Title is null ? null : new EventTitle(dto.Title),
        dto.Location is null ? null : new Location(dto.Location));

    public static OccurrenceLocalKey ToKey(OccurrenceKeyDto dto)
        => new(ParseDate(dto.Date), dto.StartTime is null ? null : ParseTime(dto.StartTime));

    // ── App → server ─────────────────────────────────────────────────────

    public static RecurrenceRuleDto ToRuleDto(RecurrenceRule rule) => new(
        rule.RuleType switch
        {
            RecurrenceType.Weekly => "WEEKLY",
            RecurrenceType.Monthly => "MONTHLY",
            _ => "YEARLY",
        },
        rule.Interval,
        rule.EndDate == NoEndDate ? null : FormatDate(rule.EndDate),
        rule.Weekly is null ? null : new WeeklyRuleDto([.. rule.Weekly.Weekdays.Distinct().OrderBy(IsoIndex).Select(FormatWeekday)]),
        rule.Monthly switch
        {
            null => null,
            DayOfMonthMonthlyRule m => new MonthlyRuleDto("DAY_OF_MONTH", Day: m.Day),
            NthWeekdayMonthlyRule m => new MonthlyRuleDto("NTH_WEEKDAY", WeekIndex: m.WeekIndex, Weekday: FormatWeekday(m.Weekday)),
            _ => new MonthlyRuleDto("LAST_DAY"),
        },
        rule.Yearly switch
        {
            null => null,
            DayOfMonthYearlyRule y => new YearlyRuleDto("DAY_OF_MONTH", y.Month, Day: y.Day),
            NthWeekdayYearlyRule y => new YearlyRuleDto("NTH_WEEKDAY", y.Month, WeekIndex: y.WeekIndex, Weekday: FormatWeekday(y.Weekday)),
            _ => throw new ArgumentException("Unknown yearly rule."),
        },
        // The named business calendar (AdjustmentRule.CalendarId) is dropped: the server shifts by
        // its day-off layers (server ADR-0032), so only the shift itself travels.
        rule.Adjustment is null ? null : new AdjustmentRuleDto(
            rule.Adjustment.Condition == AdjustmentCondition.Always ? "ALWAYS" : "HOLIDAY",
            rule.Adjustment.ShiftUnit == AdjustmentShiftUnit.CalendarDay ? "CALENDAR_DAY" : "BUSINESS_DAY",
            rule.Adjustment.ShiftAmount,
            rule.Adjustment.Action == AdjustmentAction.Cancel ? "CANCEL" : "SHIFT"));

    public static AlarmDto? ToAlarmDto(EventAlarm? alarm)
        => alarm is null ? null : new AlarmDto(alarm.IsEnabled, alarm.Notify15Min, alarm.Notify5Min, alarm.Notify1Min, alarm.NotifyAtStart);

    public static OccurrenceKeyDto ToKeyDto(OccurrenceLocalKey key)
        => new(FormatDate(key.Date), key.Time is null ? null : FormatTime(key.Time));

    public static string FormatColor(EventColorKey color) => color.ToString().ToUpperInvariant();

    public static EventColorKey ParseColor(string? value)
        => Enum.TryParse<EventColorKey>(value, ignoreCase: true, out var color) ? color : EventColorKey.Default;

    /// <summary>A new event as the server's create body. <paramref name="calendarId"/> null = the default calendar.</summary>
    public static EventCreateDto ToCreateDto(CalendarEvent ev, long? calendarId)
    {
        var (start, duration) = StartAndDuration(ev);
        return new EventCreateDto(
            ev.Title.Value,
            ev.TimeZoneId.Value,
            start,
            duration,
            Text(ev.Location?.Value),
            Text(ev.Description?.Value),
            FormatColor(ev.ColorKey),
            TaskId: null,
            ev.RecurringSchedule is null ? null : ToRuleDto(ev.RecurringSchedule.RecurrenceRule),
            ToAlarmDto(ev.Alarm),
            EventType: "EVENT",
            calendarId);
    }

    /// <summary>A single event's details and time; task, category and calendar are sent back as the server has them.</summary>
    public static EventUpdateDto ToUpdateDto(CalendarEvent ev, EventDto server)
    {
        var (start, duration) = StartAndDuration(ev);
        return new EventUpdateDto(
            ev.Title.Value,
            Text(ev.Location?.Value),
            Text(ev.Description?.Value),
            server.TaskId,
            start,
            duration,
            FormatColor(ev.ColorKey),
            ToAlarmDto(ev.Alarm),
            server.EventType,
            server.CalendarId,
            server.Version);
    }

    /// <summary>All occurrences of a recurring event; task, category and calendar are sent back as the server has them.</summary>
    public static SeriesUpdateDto ToSeriesDto(CalendarEvent ev, EventDto server)
    {
        var schedule = ev.RecurringSchedule ?? throw new ArgumentException("Not a recurring event.", nameof(ev));
        return new SeriesUpdateDto(
            ev.Title.Value,
            schedule.DurationMinutes,
            ToRuleDto(schedule.RecurrenceRule),
            schedule.AnchorUtc,
            Text(ev.Location?.Value),
            Text(ev.Description?.Value),
            server.TaskId,
            FormatColor(ev.ColorKey),
            ToAlarmDto(ev.Alarm),
            server.EventType,
            server.CalendarId,
            server.Version);
    }

    /// <summary>
    /// A move as the server stores it: start time and length are filled from the series when the
    /// app left them empty, so the app's move and the server's echo compare equal.
    /// </summary>
    public static EventMoveDto ToMoveDto(EventMove move, CalendarEvent series)
    {
        var schedule = series.RecurringSchedule ?? throw new ArgumentException("Not a recurring event.", nameof(series));
        var seriesTime = LocalSchedulePoint.LocalTimeOf(schedule.AnchorUtc, series.TimeZoneId.ToTimeZoneInfo());
        return new EventMoveDto(
            ToKeyDto(move.OccurrenceKey),
            FormatDate(move.NewDate),
            FormatTime(move.NewStartTime ?? seriesTime),
            move.NewDurationMinutes ?? schedule.DurationMinutes,
            Text(move.Title?.Value),
            Text(move.Location?.Value));
    }

    public static OccurrenceMoveDto ToMoveRequest(EventMove move, CalendarEvent series, int expectedVersion)
    {
        var echo = ToMoveDto(move, series);
        var start = LocalSchedulePoint.StartInstant(
            move.NewDate, ParseTime(echo.NewStartTime!), series.TimeZoneId.ToTimeZoneInfo()).ToUniversalTime();
        return new OccurrenceMoveDto(echo.Occurrence, start, echo.NewDurationMinutes!.Value, echo.Title, echo.Location, expectedVersion);
    }

    private static (DateTimeOffset Start, int Duration) StartAndDuration(CalendarEvent ev)
        => ev.SingleSchedule is { } single
            ? (single.StartUtc, single.DurationMinutes)
            : (ev.RecurringSchedule!.AnchorUtc, ev.RecurringSchedule.DurationMinutes);

    // ── Formats ──────────────────────────────────────────────────────────

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    public static string FormatDate(LocalDateValue date) => date.ToString();

    public static LocalDateValue ParseDate(string text)
        => LocalDateValue.FromDateOnly(DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None));

    public static string FormatTime(LocalTimeValue time)
        => time.Second == 0 ? $"{time.Hour:D2}:{time.Minute:D2}" : $"{time.Hour:D2}:{time.Minute:D2}:{time.Second:D2}";

    private static readonly string[] TimeFormats = ["HH:mm", "HH:mm:ss"];

    public static LocalTimeValue ParseTime(string text)
        => LocalTimeValue.FromTimeOnly(TimeOnly.ParseExact(text, TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None));

    private static readonly string[] WeekdayCodes = ["SU", "MO", "TU", "WE", "TH", "FR", "SA"];

    public static string FormatWeekday(Weekday weekday) => WeekdayCodes[(int)weekday];

    public static Weekday ParseWeekday(string code)
    {
        var index = Array.IndexOf(WeekdayCodes, code);
        return index >= 0 ? (Weekday)index : throw new FormatException($"Unknown weekday: {code}");
    }

    // Monday-first, as the server orders weekly days.
    private static int IsoIndex(Weekday weekday) => weekday == Weekday.Sunday ? 7 : (int)weekday;
}
