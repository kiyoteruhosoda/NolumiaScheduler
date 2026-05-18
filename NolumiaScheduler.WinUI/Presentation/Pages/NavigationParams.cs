namespace NolumiaScheduler.Presentation.Pages;

public sealed record EventEditParams(
    string? EventId = null,
    string? StartDate = null,
    int? StartMinute = null,
    string? OccurrenceDate = null,
    int? OccurrenceStartMinute = null);

public sealed record BusinessCalendarEditParams(
    string? CalendarId = null);
