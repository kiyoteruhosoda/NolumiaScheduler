using NolumiaScheduler.Domain.Repositories;
using NolumiaScheduler.Infrastructure;
using NolumiaScheduler.Infrastructure.Seeding;

namespace NolumiaScheduler.Cli;

/// <summary>Management command handlers. Each returns a process exit code.</summary>
internal static class Commands
{
    public static int Info(StorageContext ctx)
    {
        Console.WriteLine($"Data directory: {ctx.DataDirectory}");
        Console.WriteLine();

        Console.WriteLine("JSON:");
        if (Directory.Exists(ctx.EventsDirectory) || Directory.Exists(ctx.BusinessCalendarsDirectory))
        {
            var events = ctx.CreateCalendarEventRepository(StorageBackend.Json).FindAll().Count;
            var calendars = ctx.CreateBusinessCalendarRepository(StorageBackend.Json).FindAll().Count;
            Console.WriteLine($"  events: {events}, business calendars: {calendars}");
        }
        else
        {
            Console.WriteLine("  (no data)");
        }

        Console.WriteLine("SQLite:");
        if (File.Exists(ctx.SqliteDatabasePath))
        {
            var events = ctx.CreateCalendarEventRepository(StorageBackend.Sqlite).FindAll().Count;
            var calendars = ctx.CreateBusinessCalendarRepository(StorageBackend.Sqlite).FindAll().Count;
            Console.WriteLine($"  {ctx.SqliteDatabasePath}");
            Console.WriteLine($"  events: {events}, business calendars: {calendars}");
        }
        else
        {
            Console.WriteLine("  (no database)");
        }

        return 0;
    }

    public static int Migrate(StorageContext ctx, string? direction, bool force)
    {
        var (sourceBackend, targetBackend) = direction?.ToLowerInvariant() switch
        {
            "json-to-sqlite" => (StorageBackend.Json, StorageBackend.Sqlite),
            "sqlite-to-json" => (StorageBackend.Sqlite, StorageBackend.Json),
            _ => ((StorageBackend?)null, (StorageBackend?)null),
        };

        if (sourceBackend == null)
        {
            Console.Error.WriteLine("Usage: nolumia migrate <json-to-sqlite|sqlite-to-json> [--force]");
            return 1;
        }

        var targetEvents = ctx.CreateCalendarEventRepository(targetBackend!.Value);
        var targetCalendars = ctx.CreateBusinessCalendarRepository(targetBackend.Value);

        if (!force && (targetEvents.FindAll().Count > 0 || targetCalendars.FindAll().Count > 0))
        {
            Console.Error.WriteLine(
                $"Target ({targetBackend}) already contains data. Re-run with --force to overwrite by id.");
            return 2;
        }

        var report = StorageMigrator.Migrate(
            ctx.CreateCalendarEventRepository(sourceBackend.Value),
            ctx.CreateBusinessCalendarRepository(sourceBackend.Value),
            ctx.CreateAppSettingsRepository(sourceBackend.Value),
            targetEvents,
            targetCalendars,
            ctx.CreateAppSettingsRepository(targetBackend.Value));

        Console.WriteLine(
            $"Migrated {report.Events} events and {report.BusinessCalendars} business calendars " +
            $"({sourceBackend} -> {targetBackend}).");
        return 0;
    }

    public static int Seed(StorageContext ctx, string? backendName)
    {
        if (!TryParseBackend(backendName, out var backend)) return 1;

        var repo = ctx.CreateCalendarEventRepository(backend);
        var before = repo.FindAll().Count;
        DefaultEventSeeder.SeedIfEmpty(repo, TimeProvider.System);
        var after = repo.FindAll().Count;

        Console.WriteLine(after > before
            ? $"Seeded {after - before} sample events into {backend}."
            : $"{backend} already had data ({before} events); nothing seeded.");
        return 0;
    }

    public static int List(StorageContext ctx, string? backendName)
    {
        if (!TryParseBackend(backendName, out var backend)) return 1;

        var events = ctx.CreateCalendarEventRepository(backend).FindAll();
        Console.WriteLine($"{events.Count} event(s) in {backend}:");
        foreach (var ev in events.OrderBy(e => e.Title.Value, StringComparer.OrdinalIgnoreCase))
        {
            var (start, end) = ev.GetActiveDateSpan();
            Console.WriteLine($"  {ev.Kind,-9} {ev.Title.Value}  [{start} .. {end}]  ({ev.Id.Value})");
        }
        return 0;
    }

    public static int DbMigrate(StorageContext ctx)
    {
        // Touching the SQLite repository runs pending schema migrations as a side effect.
        _ = ctx.CreateCalendarEventRepository(StorageBackend.Sqlite);
        Console.WriteLine($"SQLite schema is up to date: {ctx.SqliteDatabasePath}");
        return 0;
    }

    private static bool TryParseBackend(string? name, out StorageBackend backend)
    {
        if (Enum.TryParse(name, ignoreCase: true, out backend)) return true;
        Console.Error.WriteLine("Expected a backend argument: json | sqlite");
        return false;
    }
}
