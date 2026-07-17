using System.Text.Json;
using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.Entities;
using NolumiaScheduler.Domain.Repositories;
using NolumiaScheduler.Domain.ValueObjects;
using Visibility = NolumiaScheduler.Domain.ValueObjects.Visibility;
using Location = NolumiaScheduler.Domain.ValueObjects.Location;

namespace NolumiaScheduler.Infrastructure.Json.Repositories;

public class JsonCalendarEventRepository : ICalendarEventRepository, ICalendarEventChanges
{
    public event Action? Changed;
    private readonly string _directoryPath;

    public JsonCalendarEventRepository(string directoryPath)
    {
        _directoryPath = directoryPath;
        Directory.CreateDirectory(_directoryPath);
    }

    public CalendarEvent? FindById(EventId id)
    {
        var path = GetFilePath(id);
        if (!File.Exists(path)) return null;

        var json = File.ReadAllText(path);
        var dto = JsonSerializer.Deserialize(json, AppJsonContext.Default.CalendarEventDto);
        return dto?.ToDomain();
    }

    public IReadOnlyList<CalendarEvent> FindAll()
    {
        var results = new List<CalendarEvent>();
        foreach (var file in Directory.GetFiles(_directoryPath, "*.json"))
        {
            var json = File.ReadAllText(file);
            var dto = JsonSerializer.Deserialize(json, AppJsonContext.Default.CalendarEventDto);
            if (dto != null)
                results.Add(dto.ToDomain());
        }
        return results;
    }

    public void Save(CalendarEvent calendarEvent)
    {
        var dto = CalendarEventDto.FromDomain(calendarEvent);
        var json = JsonSerializer.Serialize(dto, AppJsonContext.Default.CalendarEventDto);
        var path = GetFilePath(calendarEvent.Id);
        File.WriteAllText(path, json);
        Changed?.Invoke();
    }

    public void Delete(EventId id)
    {
        var path = GetFilePath(id);
        if (File.Exists(path))
            File.Delete(path);
        Changed?.Invoke();
    }

    private string GetFilePath(EventId id) => Path.Combine(_directoryPath, $"{id.Value}.json");
}

internal class CalendarEventDto
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Location { get; set; }
    public string Visibility { get; set; } = "Public";
    public string? EventType { get; set; }
    public string? Description { get; set; }
    public string TimeZoneId { get; set; } = "";
    public SingleScheduleDto? SingleSchedule { get; set; }
    public RecurringScheduleDto? RecurringSchedule { get; set; }
    public List<EventExceptionDto> Exceptions { get; set; } = [];
    public List<EventMoveDto> Moves { get; set; } = [];
    public AlarmDto? Alarm { get; set; }
    // Named color key (e.g. "Tomato"); null means the default event color.
    public string? Color { get; set; }
    public int Version { get; set; }
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";

    public CalendarEvent ToDomain()
    {
        var kind = Enum.Parse<EventKind>(Kind);
        var visibility = Enum.Parse<Visibility>(Visibility);

        SingleEventSchedule? single = SingleSchedule?.ToDomain();
        RecurringEventSchedule? recurring = RecurringSchedule?.ToDomain();

        var exceptions = Exceptions.Select(e => e.ToDomain()).ToList();
        var moves = Moves.Select(m => m.ToDomain()).ToList();

        EventAlarm? alarm = Alarm != null
            ? new EventAlarm(Alarm.IsEnabled, Alarm.Notify15Min, Alarm.Notify5Min, Alarm.Notify1Min, Alarm.NotifyAtStart)
            : null;

        return CalendarEvent.Reconstitute(
            new EventId(Id),
            kind,
            new EventTitle(Title),
            Location != null ? new Location(Location) : null,
            visibility,
            EventType != null ? new EventType(EventType) : null,
            Description != null ? new Description(Description) : null,
            new TimeZoneId(TimeZoneId),
            single,
            recurring,
            DateTimeOffset.Parse(CreatedAt),
            DateTimeOffset.Parse(UpdatedAt),
            exceptions,
            moves,
            new VersionNo(Version),
            alarm: alarm,
            colorKey: Color != null && Enum.TryParse<EventColorKey>(Color, out var parsedColor)
                ? parsedColor
                : EventColorKey.Default);
    }

    public static CalendarEventDto FromDomain(CalendarEvent ev)
    {
        return new CalendarEventDto
        {
            Id = ev.Id.Value,
            Kind = ev.Kind.ToString(),
            Title = ev.Title.Value,
            Location = ev.Location?.Value,
            Visibility = ev.Visibility.ToString(),
            EventType = ev.EventType?.Value,
            Description = ev.Description?.Value,
            TimeZoneId = ev.TimeZoneId.Value,
            SingleSchedule = ev.SingleSchedule != null
                ? SingleScheduleDto.FromDomain(ev.SingleSchedule)
                : null,
            RecurringSchedule = ev.RecurringSchedule != null
                ? RecurringScheduleDto.FromDomain(ev.RecurringSchedule)
                : null,
            Exceptions = [.. ev.Exceptions.Select(EventExceptionDto.FromDomain)],
            Moves = [.. ev.Moves.Select(EventMoveDto.FromDomain)],
            Alarm = ev.Alarm != null ? new AlarmDto
            {
                IsEnabled = ev.Alarm.IsEnabled,
                Notify15Min = ev.Alarm.Notify15Min,
                Notify5Min = ev.Alarm.Notify5Min,
                Notify1Min = ev.Alarm.Notify1Min,
                NotifyAtStart = ev.Alarm.NotifyAtStart
            } : null,
            Color = ev.ColorKey == EventColorKey.Default ? null : ev.ColorKey.ToString(),
            Version = ev.Version.Value,
            CreatedAt = ev.CreatedAt.ToString("O"),
            UpdatedAt = ev.UpdatedAt.ToString("O")
        };
    }
}

internal class AlarmDto
{
    public bool IsEnabled { get; set; } = true;
    public bool Notify15Min { get; set; } = true;
    public bool Notify5Min { get; set; } = true;
    public bool Notify1Min { get; set; } = true;
    // Defaults to true so events saved before this field existed still alarm at the start time.
    public bool NotifyAtStart { get; set; } = true;
}

internal class SingleScheduleDto
{
    public string StartUtc { get; set; } = "";
    public int DurationMinutes { get; set; }

    public SingleEventSchedule ToDomain()
        => new(ScheduleParse.Instant(StartUtc), DurationMinutes);

    public static SingleScheduleDto FromDomain(SingleEventSchedule schedule)
        => new()
        {
            StartUtc = ScheduleParse.FormatInstant(schedule.StartUtc),
            DurationMinutes = schedule.DurationMinutes
        };
}

internal class RecurringScheduleDto
{
    public string AnchorUtc { get; set; } = "";
    public int DurationMinutes { get; set; }
    public RecurrenceRuleDto Rule { get; set; } = new();

    public RecurringEventSchedule ToDomain()
        => new(ScheduleParse.Instant(AnchorUtc), DurationMinutes, Rule.ToDomain());

    public static RecurringScheduleDto FromDomain(RecurringEventSchedule schedule)
        => new()
        {
            AnchorUtc = ScheduleParse.FormatInstant(schedule.AnchorUtc),
            DurationMinutes = schedule.DurationMinutes,
            Rule = RecurrenceRuleDto.FromDomain(schedule.RecurrenceRule)
        };
}

internal static class ScheduleParse
{
    public static LocalDateValue Date(string s)
    {
        var d = DateOnly.Parse(s);
        return new LocalDateValue(d.Year, d.Month, d.Day);
    }

    public static LocalTimeValue Time(string s)
    {
        var t = TimeOnly.Parse(s);
        return new LocalTimeValue(t.Hour, t.Minute, t.Second);
    }

    public static DateTimeOffset Instant(string s)
        => DateTimeOffset.Parse(s, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime();

    public static string FormatInstant(DateTimeOffset instant)
        => instant.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
}

internal class RecurrenceRuleDto
{
    public string RuleType { get; set; } = "";
    public int Interval { get; set; }
    public string EndDate { get; set; } = "";
    public List<string>? Weekdays { get; set; }
    public MonthlyRuleDto? Monthly { get; set; }
    public YearlyRuleDto? Yearly { get; set; }
    public AdjustmentDto? Adjustment { get; set; }
    // Back-compat: older data stored only a Forward/Backward direction (Holiday + ±1 business day).
    public string? AdjustmentDirection { get; set; }

    public RecurrenceRule ToDomain()
    {
        var type = Enum.Parse<RecurrenceType>(RuleType);
        var endDate = DateOnly.Parse(EndDate);
        var endDateValue = new LocalDateValue(endDate.Year, endDate.Month, endDate.Day);

        WeeklyRule? weekly = Weekdays != null
            ? new WeeklyRule([.. Weekdays.Select(w => Enum.Parse<Weekday>(w))])
            : null;

        MonthlyRule? monthly = Monthly?.ToDomain();
        YearlyRule? yearly = Yearly?.ToDomain();
        AdjustmentRule? adjustment = Adjustment?.ToDomain()
            ?? (AdjustmentDirection != null
                ? new AdjustmentRule(Enum.Parse<NolumiaScheduler.Domain.ValueObjects.AdjustmentDirection>(AdjustmentDirection))
                : null);

        return new RecurrenceRule(type, Interval, endDateValue, weekly, monthly, yearly, adjustment);
    }

    public static RecurrenceRuleDto FromDomain(RecurrenceRule rule)
    {
        var dto = new RecurrenceRuleDto
        {
            RuleType = rule.RuleType.ToString(),
            Interval = rule.Interval,
            EndDate = rule.EndDate.ToString(),
            Adjustment = rule.Adjustment != null ? AdjustmentDto.FromDomain(rule.Adjustment) : null
        };

        if (rule.Weekly != null)
            dto.Weekdays = [.. rule.Weekly.Weekdays.Select(w => w.ToString())];

        if (rule.Monthly != null)
            dto.Monthly = MonthlyRuleDto.FromDomain(rule.Monthly);

        if (rule.Yearly != null)
            dto.Yearly = YearlyRuleDto.FromDomain(rule.Yearly);

        return dto;
    }
}

internal class MonthlyRuleDto
{
    public string Type { get; set; } = "";
    public int? Day { get; set; }
    public int? WeekIndex { get; set; }
    public string? Weekday { get; set; }

    public MonthlyRule ToDomain()
    {
        if (Type == "DayOfMonth")
            return new DayOfMonthMonthlyRule(Day!.Value);
        if (Type == "LastDay")
            return new LastDayOfMonthMonthlyRule();
        return new NthWeekdayMonthlyRule(WeekIndex!.Value, Enum.Parse<Weekday>(Weekday!));
    }

    public static MonthlyRuleDto FromDomain(MonthlyRule rule)
    {
        if (rule is DayOfMonthMonthlyRule dom)
            return new MonthlyRuleDto { Type = "DayOfMonth", Day = dom.Day };
        if (rule is LastDayOfMonthMonthlyRule)
            return new MonthlyRuleDto { Type = "LastDay" };
        var nth = (NthWeekdayMonthlyRule)rule;
        return new MonthlyRuleDto { Type = "NthWeekday", WeekIndex = nth.WeekIndex, Weekday = nth.Weekday.ToString() };
    }
}

internal class AdjustmentDto
{
    public string Condition { get; set; } = "Holiday";
    public string ShiftUnit { get; set; } = "BusinessDay";
    public int ShiftAmount { get; set; }
    public string? CalendarId { get; set; }
    public string Action { get; set; } = "Shift";

    public AdjustmentRule ToDomain() => new(
        Enum.Parse<AdjustmentCondition>(Condition),
        Enum.Parse<AdjustmentShiftUnit>(ShiftUnit),
        ShiftAmount,
        CalendarId != null ? new BusinessCalendarId(CalendarId) : null,
        Enum.Parse<AdjustmentAction>(Action));

    public static AdjustmentDto FromDomain(AdjustmentRule a) => new()
    {
        Condition = a.Condition.ToString(),
        ShiftUnit = a.ShiftUnit.ToString(),
        ShiftAmount = a.ShiftAmount,
        CalendarId = a.CalendarId?.Value,
        Action = a.Action.ToString()
    };
}

internal class YearlyRuleDto
{
    public string Type { get; set; } = "";
    public int? Month { get; set; }
    public int? Day { get; set; }
    public int? WeekIndex { get; set; }
    public string? Weekday { get; set; }

    public YearlyRule ToDomain()
    {
        if (Type == "DayOfMonth")
            return new DayOfMonthYearlyRule(Month!.Value, Day!.Value);
        return new NthWeekdayYearlyRule(Month!.Value, WeekIndex!.Value, Enum.Parse<Weekday>(Weekday!));
    }

    public static YearlyRuleDto FromDomain(YearlyRule rule)
    {
        if (rule is DayOfMonthYearlyRule dom)
            return new YearlyRuleDto { Type = "DayOfMonth", Month = dom.Month, Day = dom.Day };
        var nth = (NthWeekdayYearlyRule)rule;
        return new YearlyRuleDto { Type = "NthWeekday", Month = nth.Month, WeekIndex = nth.WeekIndex, Weekday = nth.Weekday.ToString() };
    }
}

internal class EventExceptionDto
{
    public string Date { get; set; } = "";
    public string? Time { get; set; }
    public string Type { get; set; } = "";
    public ExceptionOverrideDto? Override { get; set; }

    public EventException ToDomain()
    {
        var date = DateOnly.Parse(Date);
        var dateValue = new LocalDateValue(date.Year, date.Month, date.Day);
        LocalTimeValue? time = Time != null ? ParseTime(Time) : null;
        var key = new OccurrenceLocalKey(dateValue, time);

        if (Type == "Skip")
            return EventException.CreateSkip(key);

        var ov = Override!.ToDomain();
        return EventException.CreateOverride(key, ov);
    }

    public static EventExceptionDto FromDomain(EventException ex)
    {
        var dto = new EventExceptionDto
        {
            Date = ex.OccurrenceKey.Date.ToString(),
            Time = ex.OccurrenceKey.Time?.ToString(),
            Type = ex.Type.ToString()
        };

        if (ex.Override != null)
            dto.Override = ExceptionOverrideDto.FromDomain(ex.Override);

        return dto;
    }

    private static LocalTimeValue ParseTime(string s)
    {
        var t = TimeOnly.Parse(s);
        return new LocalTimeValue(t.Hour, t.Minute, t.Second);
    }
}

internal class ExceptionOverrideDto
{
    public string? Title { get; set; }
    public string? Location { get; set; }
    public string? Visibility { get; set; }
    public string? StartTime { get; set; }
    public int? DurationMinutes { get; set; }
    public bool? AlarmEnabled { get; set; }

    public ExceptionOverride ToDomain()
    {
        return new ExceptionOverride(
            Title != null ? new EventTitle(Title) : null,
            Location != null ? new Location(Location) : null,
            Visibility != null ? Enum.Parse<Visibility>(Visibility) : null,
            StartTime != null ? ScheduleParse.Time(StartTime) : null,
            DurationMinutes,
            AlarmEnabled);
    }

    public static ExceptionOverrideDto FromDomain(ExceptionOverride ov)
    {
        return new ExceptionOverrideDto
        {
            Title = ov.Title?.Value,
            Location = ov.Location?.Value,
            Visibility = ov.Visibility?.ToString(),
            StartTime = ov.StartTime?.ToString(),
            DurationMinutes = ov.DurationMinutes,
            AlarmEnabled = ov.AlarmEnabled
        };
    }
}

internal class EventMoveDto
{
    public string Date { get; set; } = "";
    public string? Time { get; set; }
    public string NewDate { get; set; } = "";
    public string? NewStartTime { get; set; }
    public int? NewDurationMinutes { get; set; }
    public string? Title { get; set; }
    public string? Location { get; set; }
    public string? Visibility { get; set; }

    public EventMove ToDomain()
    {
        var key = new OccurrenceLocalKey(ScheduleParse.Date(Date), Time != null ? ScheduleParse.Time(Time) : null);

        return new EventMove(
            key, ScheduleParse.Date(NewDate),
            NewStartTime != null ? ScheduleParse.Time(NewStartTime) : null,
            NewDurationMinutes,
            Title != null ? new EventTitle(Title) : null,
            Location != null ? new Location(Location) : null,
            Visibility != null ? Enum.Parse<Visibility>(Visibility) : null);
    }

    public static EventMoveDto FromDomain(EventMove move)
    {
        return new EventMoveDto
        {
            Date = move.OccurrenceKey.Date.ToString(),
            Time = move.OccurrenceKey.Time?.ToString(),
            NewDate = move.NewDate.ToString(),
            NewStartTime = move.NewStartTime?.ToString(),
            NewDurationMinutes = move.NewDurationMinutes,
            Title = move.Title?.Value,
            Location = move.Location?.Value,
            Visibility = move.Visibility?.ToString()
        };
    }
}
