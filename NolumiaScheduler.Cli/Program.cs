using NolumiaScheduler.Cli;
using NolumiaScheduler.Infrastructure;

var parsed = CliArgs.Parse(args);

if (parsed.Command is null or "help" or "--help" or "-h")
{
    PrintUsage();
    return parsed.Command is null ? 1 : 0;
}

var dataDir = parsed.Option("data-dir") ?? StorageContext.DefaultDataDirectory;
var ctx = new StorageContext(dataDir);

try
{
    return parsed.Command.ToLowerInvariant() switch
    {
        "info" => Commands.Info(ctx),
        "config" => Commands.ConfigShow(ctx),
        "set-backend" => Commands.SetBackend(ctx, parsed.Positional(0)),
        "migrate" => Commands.Migrate(ctx, parsed.Positional(0), parsed.HasFlag("force")),
        "seed" => Commands.Seed(ctx, parsed.Positional(0)),
        "list" => Commands.List(ctx, parsed.Positional(0)),
        "db-migrate" => Commands.DbMigrate(ctx),
        _ => Unknown(parsed.Command),
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 2;
}

static int Unknown(string command)
{
    Console.Error.WriteLine($"Unknown command: {command}");
    PrintUsage();
    return 1;
}

static void PrintUsage()
{
    Console.WriteLine("""
        nolumia - NolumiaScheduler management CLI

        Usage:
          nolumia <command> [arguments] [--data-dir <dir>]

        Commands:
          info                                 Show data location, active backend and counts
          config                               Show the active backend and config file path
          set-backend <backend>               Select the backend the app loads (restart to apply)
          migrate <direction> [--force]        Copy data between backends
                                               direction: json-to-sqlite | sqlite-to-json
                                               (skips when target has data unless --force)
          seed <backend>                       Seed sample events if the backend is empty
          list <backend>                       List events with their active date span
          db-migrate                           Apply pending SQLite schema migrations
          help                                 Show this help

        backend: json | sqlite

        Options:
          --data-dir <dir>   Override the data folder (default: per-user app data)

        Examples:
          nolumia info
          nolumia migrate json-to-sqlite
          nolumia seed sqlite --data-dir ./mydata
        """);
}
