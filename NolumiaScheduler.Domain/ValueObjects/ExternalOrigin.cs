using NolumiaScheduler.Domain.Exceptions;

namespace NolumiaScheduler.Domain.ValueObjects;

/// <summary>
/// Identifies an event imported from an external calendar (e.g. Outlook via Power Automate).
/// Events carrying an origin are read-only for the user; only the sync process may change them.
/// </summary>
public sealed record ExternalOrigin
{
    public string SourceId { get; }
    public string ExternalKey { get; }
    public DateTimeOffset? LastModified { get; }

    /// <summary>
    /// Which kind of import this is (Outlook or an iCalendar feed). Defaults to Outlook: the local
    /// import of the old app only ever read Outlook.
    /// </summary>
    public EventOrigin Kind { get; }

    public ExternalOrigin(
        string sourceId, string externalKey, DateTimeOffset? lastModified, EventOrigin kind = EventOrigin.Outlook)
    {
        if (string.IsNullOrWhiteSpace(sourceId)) throw new DomainException("sourceId is required.");
        if (string.IsNullOrWhiteSpace(externalKey)) throw new DomainException("externalKey is required.");

        SourceId = sourceId;
        ExternalKey = externalKey;
        if (kind == EventOrigin.Manual) throw new DomainException("An imported event cannot be manual.");
        LastModified = lastModified?.ToUniversalTime();
        Kind = kind;
    }
}
