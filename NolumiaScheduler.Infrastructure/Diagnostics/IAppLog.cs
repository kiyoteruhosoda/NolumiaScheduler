namespace NolumiaScheduler.Infrastructure.Diagnostics;

/// <summary>
/// Sink-agnostic diagnostic log. Implementations must never throw: the primary caller is a
/// crash handler, and an exception raised while reporting a crash would replace the real
/// fault with a useless one.
/// </summary>
public interface IAppLog
{
    void Write(AppLogLevel level, string category, string message, Exception? exception = null);
}

/// <summary>Level-named shorthands over <see cref="IAppLog.Write"/>.</summary>
public static class AppLogExtensions
{
    public static void Debug(this IAppLog log, string category, string message)
        => log.Write(AppLogLevel.Debug, category, message);

    public static void Info(this IAppLog log, string category, string message)
        => log.Write(AppLogLevel.Info, category, message);

    public static void Warning(this IAppLog log, string category, string message, Exception? exception = null)
        => log.Write(AppLogLevel.Warning, category, message, exception);

    public static void Error(this IAppLog log, string category, string message, Exception? exception = null)
        => log.Write(AppLogLevel.Error, category, message, exception);

    public static void Fatal(this IAppLog log, string category, string message, Exception? exception = null)
        => log.Write(AppLogLevel.Fatal, category, message, exception);
}
