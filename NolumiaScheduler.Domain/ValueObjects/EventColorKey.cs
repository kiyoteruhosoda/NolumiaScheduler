namespace NolumiaScheduler.Domain.ValueObjects;

/// <summary>
/// Named color key assigned to an event. The domain only knows the key;
/// the presentation layer maps each key to actual theme-aware colors.
/// <see cref="Default"/> means "no explicit color" — the standard event color is used.
/// </summary>
public enum EventColorKey
{
    Default,
    Tomato,
    Tangerine,
    Banana,
    Basil,
    Sage,
    Peacock,
    Blueberry,
    Lavender,
    Grape,
    Graphite
}
