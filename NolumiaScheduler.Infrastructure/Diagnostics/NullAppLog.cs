namespace NolumiaScheduler.Infrastructure.Diagnostics;

/// <summary>
/// Discards every record. Used as the default for <see cref="AppLog.Current"/> so code that
/// logs unconditionally (including code under test) works before any sink is installed.
/// </summary>
public sealed class NullAppLog : IAppLog
{
    public static readonly NullAppLog Instance = new();

    private NullAppLog() { }

    public void Write(AppLogLevel level, string category, string message, Exception? exception = null)
    {
    }
}
