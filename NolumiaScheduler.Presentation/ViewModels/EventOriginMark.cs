using NolumiaScheduler.Domain.ValueObjects;
using NolumiaScheduler.Presentation.Resources.Strings;

namespace NolumiaScheduler.Presentation.ViewModels;

/// <summary>
/// The mark on imported events (task #550; the server's ADR-0057): a small box with "O" for Outlook
/// and "ics" for an iCalendar feed. Events made by hand get none. The mark is a shape with text, not a
/// color, and carries a name for screen readers.
/// </summary>
public static class EventOriginMark
{
    public static bool Has(EventOrigin origin) => origin != EventOrigin.Manual;

    /// <summary>The text in the box ("" for hand-made events).</summary>
    public static string Text(EventOrigin origin) => origin switch
    {
        EventOrigin.Outlook => "O",
        EventOrigin.Feed => "ics",
        _ => "",
    };

    /// <summary>The name read out by screen readers (also the tooltip).</summary>
    public static string Name(EventOrigin origin) => origin switch
    {
        EventOrigin.Outlook => AppResources.OriginOutlookName,
        EventOrigin.Feed => AppResources.OriginFeedName,
        _ => "",
    };
}
