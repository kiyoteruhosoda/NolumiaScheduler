namespace NolumiaScheduler.Infrastructure.Sqlite.Repositories;

/// <summary>
/// Database row shape for <c>calendar_events</c>. Kept separate from the
/// <see cref="NolumiaScheduler.Domain.Aggregates.CalendarEvent"/> domain entity per
/// the persistence rules; the mapper converts between the two.
/// <para>
/// <see cref="SpanStartDay"/>/<see cref="SpanEndDay"/> are the indexed active-span
/// bounds used for period queries. They are written on save and are 0 on rows read for
/// other purposes (the domain mapping only needs the payload).
/// </para>
/// </summary>
internal sealed record CalendarEventRow(
    string Id,
    string Payload,
    string UpdatedAt,
    int SpanStartDay = 0,
    int SpanEndDay = 0);
