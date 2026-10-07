namespace NolumiaScheduler.Domain.ExternalCalendars;

/// <summary>
/// One appointment read from an external calendar. Timed appointments carry UTC instants;
/// all-day appointments carry plain dates (no timezone conversion) in
/// <see cref="AllDayStart"/>/<see cref="AllDayEndExclusive"/>.
/// </summary>
public sealed record ExternalAppointment(
    string Key,
    string Title,
    bool IsAllDay,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    DateOnly AllDayStart,
    DateOnly AllDayEndExclusive,
    string? Location,
    int? ReminderMinutes,
    bool IsPrivate,
    DateTimeOffset? LastModified);

/// <summary>
/// A read-only external calendar feed.
/// </summary>
public interface IExternalCalendarSource
{
    /// <summary>Stable identifier stored on imported events (e.g. "outlook").</summary>
    string SourceId { get; }

    /// <summary>
    /// Reads the current snapshot. Returns null when the feed is unavailable (missing or
    /// unreadable), in which case previously imported events must be kept unchanged.
    /// </summary>
    IReadOnlyList<ExternalAppointment>? ReadSnapshot();
}

