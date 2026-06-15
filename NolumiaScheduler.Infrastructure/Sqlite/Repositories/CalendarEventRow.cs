namespace NolumiaScheduler.Infrastructure.Sqlite.Repositories;

/// <summary>
/// Database row shape for <c>calendar_events</c>. Kept separate from the
/// <see cref="NolumiaScheduler.Domain.Aggregates.CalendarEvent"/> domain entity per
/// the persistence rules; the mapper converts between the two.
/// </summary>
internal sealed record CalendarEventRow(string Id, string Payload, string UpdatedAt);
