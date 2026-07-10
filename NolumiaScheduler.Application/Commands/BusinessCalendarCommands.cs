using NolumiaScheduler.Domain.ValueObjects;

namespace NolumiaScheduler.Application.Commands;

public sealed record CreateBusinessCalendarCommand(
    string Name,
    string TimeZone,
    IReadOnlyList<Weekday> Workdays,
    bool ShiftOnHolidaysOnly = false,
    bool IsEnabled = true);

public sealed record UpdateBusinessCalendarCommand(
    string CalendarId,
    string Name,
    IReadOnlyList<Weekday> Workdays,
    bool ShiftOnHolidaysOnly = false,
    bool IsEnabled = true);

public sealed record AddHolidayCommand(
    string CalendarId,
    LocalDateValue Date,
    string? Name);

public sealed record RemoveHolidayCommand(
    string CalendarId,
    LocalDateValue Date);

public sealed record DeleteBusinessCalendarCommand(
    string CalendarId);
