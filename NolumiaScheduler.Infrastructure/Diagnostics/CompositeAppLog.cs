namespace NolumiaScheduler.Infrastructure.Diagnostics;

/// <summary>
/// Fans a record out to several sinks (e.g. the rolling file plus the Windows Event Log).
/// A sink that throws is skipped so one broken sink cannot silence the others.
/// </summary>
public sealed class CompositeAppLog : IAppLog
{
    private readonly IAppLog[] _sinks;

    public CompositeAppLog(params IAppLog[] sinks) => _sinks = sinks;

    public void Write(AppLogLevel level, string category, string message, Exception? exception = null)
    {
        foreach (var sink in _sinks)
        {
            try
            {
                sink.Write(level, category, message, exception);
            }
            catch
            {
                // Keep going: losing one sink must not cost us the record everywhere.
            }
        }
    }
}
