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

    public ExternalOrigin(string sourceId, string externalKey, DateTimeOffset? lastModified)
    {
        if (string.IsNullOrWhiteSpace(sourceId)) throw new DomainException("sourceId is required.");
        if (string.IsNullOrWhiteSpace(externalKey)) throw new DomainException("externalKey is required.");

        SourceId = sourceId;
        ExternalKey = externalKey;
        LastModified = lastModified?.ToUniversalTime();
    }
}
