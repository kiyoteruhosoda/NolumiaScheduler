using System.Globalization;
using System.Text.Json;
using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.Entities;
using NolumiaScheduler.Domain.ValueObjects;
using DomainLocation = NolumiaScheduler.Domain.ValueObjects.Location;
using DomainVisibility = NolumiaScheduler.Domain.ValueObjects.Visibility;

namespace NolumiaSchedulerTest.E2E;

internal static class JsonScenarioLoader
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static string ResolvePath(string fileName)
        => Path.Combine(AppContext.BaseDirectory, "Inputs", "Json", fileName);

    public static CalendarEvent LoadEvent(string fileName)
    {
        var path = ResolvePath(fileName);
        using var stream = File.OpenRead(path);
        var dto = JsonSerializer.Deserialize<EventDto>(stream, Options)
            ?? throw new InvalidOperationException($"Failed to parse event JSON: {path}");
        return MapEvent(dto);
    }

    public static BusinessCalendar LoadCalendar(string fileName)
    {
        var path = ResolvePath(fileName);
        using var stream = File.OpenRead(path);
        var dto = JsonSerializer.Deserialize<CalendarDto>(stream, Options)
            ?? throw new InvalidOperationException($"Failed to parse calendar JSON: {path}");
        return MapCalendar(dto);
    }

    private static CalendarEvent MapEvent(EventDto dto)
    {
        var id = new EventId(Required(dto.Id, "id"));
        var title = new EventTitle(Required(dto.Title, "title"));
        var location = string.IsNullOrEmpty(dto.Location) ? null : new DomainLocation(dto.Location);
        var visibility = ParseVisibility(Required(dto.Visibility, "visibility"));
        var eventType = string.IsNullOrEmpty(dto.EventType) ? null : new EventType(dto.EventType);
        var description = string.IsNullOrEmpty(dto.Description) ? null : new Description(dto.Description);
        var tz = new TimeZoneId(Required(dto.Timezone, "timezone"));
        var createdAt = ParseDateTimeOffset(Required(dto.CreatedAt, "createdAt"));
        var updatedAt = ParseDateTimeOffset(Required(dto.UpdatedAt, "updatedAt"));
        var version = new VersionNo(dto.Version <= 0 ? 1 : dto.Version);

        if (string.Equals(dto.Kind, "Single", StringComparison.OrdinalIgnoreCase))
        {
            // Scenario JSON keeps the legacy start/end shape; translate it into the duration model
            // (docs/time-model.md). The wall-clock start is taken in the event's timezone.
            var start = ParseDateTimeOffset(Required(dto.Start, "start"));
            var end = ParseDateTimeOffset(Required(dto.End, "end"));
            var localStart = TimeZoneInfo.ConvertTime(start, tz.ToTimeZoneInfo());
            var startDate = new LocalDateValue(localStart.Year, localStart.Month, localStart.Day);
            var startTime = new LocalTimeValue(localStart.Hour, localStart.Minute, localStart.Second);
            var duration = (int)(end - start).TotalMinutes;
            var schedule = new SingleEventSchedule(startDate, startTime, duration);
            return CalendarEvent.Reconstitute(
                id, EventKind.Single, title, location, visibility, eventType, description,
                tz, schedule, recurringSchedule: null,
                createdAt, updatedAt,
                exceptions: [], moves: [], version);
        }

        if (string.Equals(dto.Kind, "Recurring", StringComparison.OrdinalIgnoreCase))
        {
            var startDate = ParseLocalDate(Required(dto.StartDate, "startDate"));
            var startTime = string.IsNullOrEmpty(dto.StartTime) ? new LocalTimeValue(0, 0, 0) : ParseLocalTime(dto.StartTime);
            var duration = DurationFromTimes(dto.AllDay, dto.StartTime, dto.EndTime) ?? 24 * 60;
            var rule = MapRecurrenceRule(Required(dto.Recurrence, "recurrence"));
            var schedule = new RecurringEventSchedule(startDate, startTime, duration, rule);

            var exceptions = (dto.Exceptions ?? [])
                .Select(e => MapException(e, dto.AllDay))
                .ToList();
            var moves = (dto.Moves ?? [])
                .Select(m => MapMove(m, dto.AllDay))
                .ToList();

            return CalendarEvent.Reconstitute(
                id, EventKind.Recurring, title, location, visibility, eventType, description,
                tz, singleSchedule: null, recurringSchedule: schedule,
                createdAt, updatedAt, exceptions, moves, version);
        }

        throw new InvalidOperationException($"Unknown event kind: {dto.Kind}");
    }

    private static RecurrenceRule MapRecurrenceRule(RecurrenceDto dto)
    {
        var endDate = ParseLocalDate(Required(dto.EndDate, "recurrence.endDate"));
        var ruleType = ParseRecurrenceType(Required(dto.RuleType, "recurrence.ruleType"));
        var interval = dto.Interval <= 0 ? 1 : dto.Interval;

        WeeklyRule? weekly = null;
        MonthlyRule? monthly = null;
        YearlyRule? yearly = null;

        switch (ruleType)
        {
            case RecurrenceType.Weekly:
                if (dto.Weekly is null || dto.Weekly.Weekdays is null || dto.Weekly.Weekdays.Count == 0)
                    throw new InvalidOperationException("recurrence.weekly.weekdays is required for WEEKLY rule.");
                weekly = new WeeklyRule(dto.Weekly.Weekdays.Select(ParseWeekday).ToList());
                break;
            case RecurrenceType.Monthly:
                if (dto.Monthly is null) throw new InvalidOperationException("recurrence.monthly is required for MONTHLY rule.");
                monthly = MapMonthlyRule(dto.Monthly);
                break;
            case RecurrenceType.Yearly:
                if (dto.Yearly is null) throw new InvalidOperationException("recurrence.yearly is required for YEARLY rule.");
                yearly = MapYearlyRule(dto.Yearly);
                break;
        }

        var adjustment = dto.Adjustment is null ? null : MapAdjustment(dto.Adjustment);

        return new RecurrenceRule(ruleType, interval, endDate, weekly, monthly, yearly, adjustment);
    }

    private static MonthlyRule MapMonthlyRule(MonthlyRuleDto dto)
    {
        var mode = Required(dto.Mode, "monthly.mode");
        if (string.Equals(mode, "DAY_OF_MONTH", StringComparison.OrdinalIgnoreCase))
        {
            if (dto.Day is null) throw new InvalidOperationException("monthly.day is required for DAY_OF_MONTH.");
            return new DayOfMonthMonthlyRule(dto.Day.Value);
        }

        if (string.Equals(mode, "NTH_WEEKDAY", StringComparison.OrdinalIgnoreCase))
        {
            if (dto.WeekIndex is null) throw new InvalidOperationException("monthly.weekIndex is required for NTH_WEEKDAY.");
            if (string.IsNullOrEmpty(dto.Weekday)) throw new InvalidOperationException("monthly.weekday is required for NTH_WEEKDAY.");
            return new NthWeekdayMonthlyRule(dto.WeekIndex.Value, ParseWeekday(dto.Weekday));
        }

        throw new InvalidOperationException($"Unknown monthly.mode: {mode}");
    }

    private static YearlyRule MapYearlyRule(YearlyRuleDto dto)
    {
        var mode = Required(dto.Mode, "yearly.mode");
        if (string.Equals(mode, "DAY_OF_MONTH", StringComparison.OrdinalIgnoreCase))
        {
            if (dto.Month is null) throw new InvalidOperationException("yearly.month is required for DAY_OF_MONTH.");
            if (dto.Day is null) throw new InvalidOperationException("yearly.day is required for DAY_OF_MONTH.");
            return new DayOfMonthYearlyRule(dto.Month.Value, dto.Day.Value);
        }

        if (string.Equals(mode, "NTH_WEEKDAY", StringComparison.OrdinalIgnoreCase))
        {
            if (dto.Month is null) throw new InvalidOperationException("yearly.month is required for NTH_WEEKDAY.");
            if (dto.WeekIndex is null) throw new InvalidOperationException("yearly.weekIndex is required for NTH_WEEKDAY.");
            if (string.IsNullOrEmpty(dto.Weekday)) throw new InvalidOperationException("yearly.weekday is required for NTH_WEEKDAY.");
            return new NthWeekdayYearlyRule(dto.Month.Value, dto.WeekIndex.Value, ParseWeekday(dto.Weekday));
        }

        throw new InvalidOperationException($"Unknown yearly.mode: {mode}");
    }

    private static AdjustmentRule MapAdjustment(AdjustmentDto dto)
    {
        var condition = ParseCondition(Required(dto.Condition, "adjustment.condition"));
        var shiftUnit = ParseShiftUnit(Required(dto.ShiftUnit, "adjustment.shiftUnit"));
        var calendarId = string.IsNullOrEmpty(dto.CalendarId)
            ? null
            : new BusinessCalendarId(dto.CalendarId);

        if (shiftUnit == AdjustmentShiftUnit.BusinessDay && calendarId is null)
            throw new InvalidOperationException("adjustment.calendarId is required when shiftUnit is BUSINESS_DAY.");

        return new AdjustmentRule(condition, shiftUnit, dto.ShiftAmount, calendarId);
    }

    private static AdjustmentCondition ParseCondition(string value)
    {
        return value.ToUpperInvariant() switch
        {
            "HOLIDAY" => AdjustmentCondition.Holiday,
            _ => throw new InvalidOperationException($"Unknown adjustment.condition: {value}"),
        };
    }

    private static AdjustmentShiftUnit ParseShiftUnit(string value)
    {
        return value.ToUpperInvariant() switch
        {
            "BUSINESS_DAY" => AdjustmentShiftUnit.BusinessDay,
            "CALENDAR_DAY" => AdjustmentShiftUnit.CalendarDay,
            "DAY" => AdjustmentShiftUnit.CalendarDay,
            _ => throw new InvalidOperationException($"Unknown adjustment.shiftUnit: {value}"),
        };
    }

    private static EventException MapException(ExceptionDto dto, bool allDay)
    {
        var (date, time) = ParseOccurrenceLocal(Required(dto.OccurrenceLocal, "exception.occurrenceLocal"));
        var key = new OccurrenceLocalKey(date, time);
        var action = Required(dto.Action, "exception.action");

        if (string.Equals(action, "SKIP", StringComparison.OrdinalIgnoreCase))
            return EventException.CreateSkip(key);

        if (string.Equals(action, "OVERRIDE", StringComparison.OrdinalIgnoreCase))
        {
            var ov = dto.Override
                ?? throw new InvalidOperationException("exception.override is required for OVERRIDE.");
            var overrideValue = MapOverride(ov, allDay);
            return EventException.CreateOverride(key, overrideValue);
        }

        throw new InvalidOperationException($"Unknown exception.action: {action}");
    }

    private static ExceptionOverride MapOverride(OverrideDto dto, bool allDay)
    {
        var hasTimes = !allDay && !string.IsNullOrEmpty(dto.StartTime) && !string.IsNullOrEmpty(dto.EndTime);
        return new ExceptionOverride(
            title: string.IsNullOrEmpty(dto.Title) ? null : new EventTitle(dto.Title),
            location: string.IsNullOrEmpty(dto.Location) ? null : new DomainLocation(dto.Location),
            visibility: string.IsNullOrEmpty(dto.Visibility) ? null : ParseVisibility(dto.Visibility),
            startTime: hasTimes ? ParseLocalTime(dto.StartTime!) : null,
            durationMinutes: hasTimes ? DurationFromTimes(false, dto.StartTime, dto.EndTime) : null);
    }

    private static EventMove MapMove(MoveDto dto, bool allDay)
    {
        var (date, time) = ParseOccurrenceLocal(Required(dto.OccurrenceLocal, "move.occurrenceLocal"));
        var key = new OccurrenceLocalKey(date, time);
        var newDate = ParseLocalDate(Required(dto.NewDate, "move.newDate"));

        var hasTimes = !allDay && !string.IsNullOrEmpty(dto.NewStartTime) && !string.IsNullOrEmpty(dto.NewEndTime);
        return new EventMove(
            occurrenceKey: key,
            newDate: newDate,
            newStartTime: hasTimes ? ParseLocalTime(dto.NewStartTime!) : null,
            newDurationMinutes: hasTimes ? DurationFromTimes(false, dto.NewStartTime, dto.NewEndTime) : null,
            title: string.IsNullOrEmpty(dto.Title) ? null : new EventTitle(dto.Title),
            location: string.IsNullOrEmpty(dto.Location) ? null : new DomainLocation(dto.Location),
            visibility: string.IsNullOrEmpty(dto.Visibility) ? null : ParseVisibility(dto.Visibility));
    }

    // Translates a legacy start/end time pair into a duration in minutes. Returns null when the
    // pair is absent (all-day or unspecified); end on/before start is read as crossing midnight.
    private static int? DurationFromTimes(bool allDay, string? startStr, string? endStr)
    {
        if (allDay || string.IsNullOrEmpty(startStr) || string.IsNullOrEmpty(endStr)) return null;
        var s = ParseLocalTime(startStr);
        var e = ParseLocalTime(endStr);
        var minutes = ((e.Hour * 60) + e.Minute) - ((s.Hour * 60) + s.Minute);
        return minutes > 0 ? minutes : minutes + (24 * 60);
    }

    private static BusinessCalendar MapCalendar(CalendarDto dto)
    {
        var id = new BusinessCalendarId(Required(dto.Id, "id"));
        var name = Required(dto.Name, "name");
        var tz = new TimeZoneId(Required(dto.Timezone, "timezone"));
        var workdays = (dto.WorkdaysOfWeek ?? []).Select(ParseWeekday).ToList();
        var holidays = (dto.Holidays ?? [])
            .Select(h => new Holiday(ParseLocalDate(Required(h.Date, "holiday.date")), h.Name))
            .ToList();
        return new BusinessCalendar(id, name, tz, workdays, holidays);
    }

    private static DomainVisibility ParseVisibility(string value)
    {
        if (Enum.TryParse<DomainVisibility>(value, ignoreCase: true, out var result))
            return result;
        throw new InvalidOperationException($"Unknown visibility: {value}");
    }

    private static RecurrenceType ParseRecurrenceType(string value)
    {
        return value.ToUpperInvariant() switch
        {
            "WEEKLY" => RecurrenceType.Weekly,
            "MONTHLY" => RecurrenceType.Monthly,
            "YEARLY" => RecurrenceType.Yearly,
            _ => throw new InvalidOperationException($"Unsupported recurrence ruleType: {value}"),
        };
    }

    private static Weekday ParseWeekday(string code)
    {
        return code.ToUpperInvariant() switch
        {
            "MO" => Weekday.Monday,
            "TU" => Weekday.Tuesday,
            "WE" => Weekday.Wednesday,
            "TH" => Weekday.Thursday,
            "FR" => Weekday.Friday,
            "SA" => Weekday.Saturday,
            "SU" => Weekday.Sunday,
            _ => throw new InvalidOperationException($"Unknown weekday code: {code}"),
        };
    }

    private static LocalDateValue ParseLocalDate(string value)
    {
        var d = DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        return LocalDateValue.FromDateOnly(d);
    }

    private static LocalTimeValue ParseLocalTime(string value)
    {
        var t = TimeOnly.ParseExact(value, ["HH:mm:ss", "HH:mm"], CultureInfo.InvariantCulture);
        return LocalTimeValue.FromTimeOnly(t);
    }

    private static DateTimeOffset ParseDateTimeOffset(string value)
        => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal);

    private static (LocalDateValue Date, LocalTimeValue Time) ParseOccurrenceLocal(string value)
    {
        // Format: "yyyy-MM-ddTHH:mm:ss"
        var dt = DateTime.ParseExact(value, "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        return (
            new LocalDateValue(dt.Year, dt.Month, dt.Day),
            new LocalTimeValue(dt.Hour, dt.Minute, dt.Second));
    }

    private static string Required(string? value, string field)
        => string.IsNullOrEmpty(value)
            ? throw new InvalidOperationException($"Required field missing: {field}")
            : value;

    private static T Required<T>(T? value, string field) where T : class
        => value ?? throw new InvalidOperationException($"Required field missing: {field}");

    // ---------- DTOs ----------

    private sealed class EventDto
    {
        public string? Id { get; set; }
        public string? Kind { get; set; }
        public string? Title { get; set; }
        public string? Location { get; set; }
        public string? Visibility { get; set; }
        public string? EventType { get; set; }
        public string? Description { get; set; }
        public string? Timezone { get; set; }
        public bool AllDay { get; set; }

        // Single
        public string? Start { get; set; }
        public string? End { get; set; }

        // Recurring
        public string? StartDate { get; set; }
        public string? StartTime { get; set; }
        public string? EndTime { get; set; }
        public RecurrenceDto? Recurrence { get; set; }
        public List<ExceptionDto>? Exceptions { get; set; }
        public List<MoveDto>? Moves { get; set; }

        public string? CreatedAt { get; set; }
        public string? UpdatedAt { get; set; }
        public int Version { get; set; }
    }

    private sealed class RecurrenceDto
    {
        public string? RuleType { get; set; }
        public int Interval { get; set; }
        public string? EndDate { get; set; }
        public WeeklyRuleDto? Weekly { get; set; }
        public MonthlyRuleDto? Monthly { get; set; }
        public YearlyRuleDto? Yearly { get; set; }
        public AdjustmentDto? Adjustment { get; set; }
    }

    private sealed class WeeklyRuleDto
    {
        public List<string>? Weekdays { get; set; }
    }

    private sealed class MonthlyRuleDto
    {
        public string? Mode { get; set; }
        public int? Day { get; set; }
        public int? WeekIndex { get; set; }
        public string? Weekday { get; set; }
    }

    private sealed class YearlyRuleDto
    {
        public string? Mode { get; set; }
        public int? Month { get; set; }
        public int? Day { get; set; }
        public int? WeekIndex { get; set; }
        public string? Weekday { get; set; }
    }

    private sealed class AdjustmentDto
    {
        public string? Condition { get; set; }
        public string? ShiftUnit { get; set; }
        public int ShiftAmount { get; set; }
        public string? CalendarId { get; set; }
    }

    private sealed class ExceptionDto
    {
        public string? OccurrenceLocal { get; set; }
        public string? Action { get; set; }
        public OverrideDto? Override { get; set; }
    }

    private sealed class OverrideDto
    {
        public string? Title { get; set; }
        public string? Location { get; set; }
        public string? Visibility { get; set; }
        public string? StartTime { get; set; }
        public string? EndTime { get; set; }
    }

    private sealed class MoveDto
    {
        public string? OccurrenceLocal { get; set; }
        public string? NewDate { get; set; }
        public string? NewStartTime { get; set; }
        public string? NewEndTime { get; set; }
        public string? Title { get; set; }
        public string? Location { get; set; }
        public string? Visibility { get; set; }
    }

    private sealed class CalendarDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Timezone { get; set; }
        public List<string>? WorkdaysOfWeek { get; set; }
        public List<HolidayDto>? Holidays { get; set; }
    }

    private sealed class HolidayDto
    {
        public string? Date { get; set; }
        public string? Name { get; set; }
    }
}
