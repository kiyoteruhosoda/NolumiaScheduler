namespace NolumiaScheduler.Infrastructure.Diagnostics;

/// <summary>
/// Severity of a diagnostic log record. Ordered so sinks can filter with a simple
/// <c>level &lt; minimum</c> comparison.
/// </summary>
public enum AppLogLevel
{
    /// <summary>Verbose tracing, off by default in shipped builds.</summary>
    Debug = 0,

    /// <summary>Normal lifecycle milestones (startup, resume, shutdown).</summary>
    Info = 1,

    /// <summary>Something unexpected that the app recovered from.</summary>
    Warning = 2,

    /// <summary>An operation failed; the app keeps running.</summary>
    Error = 3,

    /// <summary>The process is going down (or came back from a crash).</summary>
    Fatal = 4,
}
