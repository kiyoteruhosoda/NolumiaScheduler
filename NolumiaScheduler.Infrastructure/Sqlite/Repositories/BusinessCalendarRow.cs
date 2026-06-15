namespace NolumiaScheduler.Infrastructure.Sqlite.Repositories;

/// <summary>Database row shape for <c>business_calendars</c>.</summary>
internal sealed record BusinessCalendarRow(string Id, string Payload);
