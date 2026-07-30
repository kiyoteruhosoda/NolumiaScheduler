using System.Globalization;

namespace NolumiaScheduler.Infrastructure.Diagnostics;

/// <summary>
/// The persisted state of one run of the app, as written by <see cref="AppSessionMarker"/>.
/// Read back on the next start to tell a clean shutdown apart from a silent death.
/// </summary>
/// <param name="ProcessId">OS process id of that run.</param>
/// <param name="AppVersion">Build identity (git describe) of that run.</param>
/// <param name="StartedAt">When the run began.</param>
/// <param name="LastHeartbeat">Last time the run was known to be alive.</param>
/// <param name="LastEvent">Last lifecycle event the run recorded (e.g. <c>resume</c>).</param>
/// <param name="CleanExit">Whether the run reached an orderly shutdown.</param>
/// <param name="ExitReason">Why the run ended, when known.</param>
public sealed record AppSessionSnapshot(
    int ProcessId,
    string AppVersion,
    DateTimeOffset StartedAt,
    DateTimeOffset LastHeartbeat,
    string LastEvent,
    bool CleanExit,
    string ExitReason)
{
    /// <summary>
    /// How long the run had been unaccounted for when it ended — the window the fault happened
    /// in. Small values mean the process vanished between two heartbeats.
    /// </summary>
    public TimeSpan Uptime => LastHeartbeat - StartedAt;

    /// <summary>Single-line summary for the log and the Windows Event Log entry.</summary>
    public string Describe() => string.Format(
        CultureInfo.InvariantCulture,
        "pid={0} version={1} startedAt={2:yyyy-MM-dd HH:mm:ss zzz} lastHeartbeat={3:yyyy-MM-dd HH:mm:ss zzz} " +
        "uptime={4:g} lastEvent={5} cleanExit={6} exitReason={7}",
        ProcessId, AppVersion, StartedAt, LastHeartbeat, Uptime, LastEvent, CleanExit,
        ExitReason.Length == 0 ? "(none)" : ExitReason);
}
