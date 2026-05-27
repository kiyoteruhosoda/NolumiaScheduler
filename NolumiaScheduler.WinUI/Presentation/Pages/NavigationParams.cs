namespace NolumiaScheduler.WinUI.Presentation.Pages;

public sealed record EventEditParams(
    string? EventId = null,
    string? StartDate = null,
    int? StartMinute = null,
    int? EndMinute = null,
    string? OccurrenceDate = null,
    int? OccurrenceStartMinute = null,
    int? OccurrenceEndMinute = null);

public sealed record BusinessCalendarEditParams(
    string? CalendarId = null);
