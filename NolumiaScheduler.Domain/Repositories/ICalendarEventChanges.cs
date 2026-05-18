namespace NolumiaScheduler.Domain.Repositories;

/// <summary>
/// Implemented by repositories that raise change notifications.
/// Separates the reactive concern from the core repository contract.
/// </summary>
public interface ICalendarEventChanges
{
    event Action? Changed;
}
