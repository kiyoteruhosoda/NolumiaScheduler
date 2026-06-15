using NolumiaScheduler.Domain.Repositories;

namespace NolumiaScheduler.Infrastructure;

/// <summary>Counts copied during a storage migration.</summary>
public sealed record StorageMigrationReport(int Events, int BusinessCalendars);

/// <summary>
/// Copies all aggregates and settings from one persistence backend to another.
/// Works purely through the domain repository interfaces, so it is backend-agnostic
/// (JSON to SQLite, SQLite to JSON, or any future backend) and easy to unit test.
/// The target is written via the same Save path the app uses, so existing data in the
/// target is upserted by id rather than duplicated.
/// </summary>
public static class StorageMigrator
{
    public static StorageMigrationReport Migrate(
        ICalendarEventRepository sourceEvents,
        IBusinessCalendarRepository sourceBusinessCalendars,
        IAppSettingsRepository sourceSettings,
        ICalendarEventRepository targetEvents,
        IBusinessCalendarRepository targetBusinessCalendars,
        IAppSettingsRepository targetSettings)
    {
        var events = 0;
        foreach (var ev in sourceEvents.FindAll())
        {
            targetEvents.Save(ev);
            events++;
        }

        var calendars = 0;
        foreach (var calendar in sourceBusinessCalendars.FindAll())
        {
            targetBusinessCalendars.Save(calendar);
            calendars++;
        }

        targetSettings.SaveThemeMode(sourceSettings.GetThemeMode());
        targetSettings.SaveLanguage(sourceSettings.GetLanguage());
        targetSettings.SaveStartupView(sourceSettings.GetStartupView());

        return new StorageMigrationReport(events, calendars);
    }
}
