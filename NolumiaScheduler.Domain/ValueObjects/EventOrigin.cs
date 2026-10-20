namespace NolumiaScheduler.Domain.ValueObjects;

/// <summary>
/// Where an event came from, so the calendar can mark imported events apart from the ones made by
/// hand (task #550; the server's ADR-0057). Imported events (<see cref="ExternalOrigin"/>) are read-only.
/// </summary>
public enum EventOrigin
{
    /// <summary>Made by hand in Scheduler (no mark).</summary>
    Manual,

    /// <summary>Imported from Outlook (the "Outlook" mark).</summary>
    Outlook,

    /// <summary>Imported from an iCalendar (.ics) file or URL (the "imported" mark).</summary>
    Feed,
}
