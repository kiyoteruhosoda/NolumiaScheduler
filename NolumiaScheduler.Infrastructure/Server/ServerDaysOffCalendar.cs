using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.ValueObjects;

namespace NolumiaScheduler.Infrastructure.Server;

/// <summary>
/// The app's business calendar rebuilt from the server's day-off layers (decision 7; server
/// ADR-0029/0032), replacing the app's own business-calendar screens. It reproduces the server's
/// rule exactly so the app expands the same occurrences the server does:
/// <list type="bullet">
/// <item>workdays = the workweek layer's weekdays (Monday–Friday when the server has none);</item>
/// <item>holidays = days in a layer that "counts as a day off" (company, personal, national holidays) —
/// the weekly non-working days are <em>not</em> holidays, as on the server;</item>
/// <item>a business day is a workday that is not a holiday (<see cref="BusinessCalendar.ShiftOnHolidaysOnly"/> = false).</item>
/// </list>
/// </summary>
public static class ServerDaysOffCalendar
{
    public const string Id = "server-days-off";
    public const string WeeklyReason = "WEEKLY";

    private static readonly Weekday[] MondayToFriday =
        [Weekday.Monday, Weekday.Tuesday, Weekday.Wednesday, Weekday.Thursday, Weekday.Friday];

    public static BusinessCalendar Build(
        IReadOnlyList<CalendarDto> calendars,
        IReadOnlyList<DayOffMarkDto> marks,
        string timeZoneId)
    {
        var workweek = calendars.FirstOrDefault(c => c.Kind == "WORKWEEK" && c.Workdays is not null);
        IEnumerable<Weekday> workdays = workweek is null
            ? MondayToFriday
            : workweek.Workdays!.Select(ServerEventMapper.ParseWeekday).ToArray();

        var holidays = marks
            .Where(m => m.Reason != WeeklyReason && m.CountsAsDayOff)
            .GroupBy(m => m.Date)
            .Select(g => new Holiday(ServerEventMapper.ParseDate(g.Key), g.Select(m => m.Name).FirstOrDefault(n => n is not null)));

        return new BusinessCalendar(
            new BusinessCalendarId(Id),
            "Days off (server)",
            new TimeZoneId(timeZoneId),
            workdays,
            holidays,
            shiftOnHolidaysOnly: false,
            isEnabled: true);
    }
}
