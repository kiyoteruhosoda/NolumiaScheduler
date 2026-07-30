using System.Globalization;
using System.Text;

namespace NolumiaScheduler.Infrastructure.Diagnostics;

/// <summary>
/// Records "this run is alive" to a small file, and flags whether the run ended in an orderly
/// way.
/// <para>
/// This exists because the hardest failure to diagnose is the one that leaves nothing behind: a
/// native crash inside the XAML/composition layer, an OS kill, or a hard power loss produces no
/// managed exception and therefore no log entry at all. The marker inverts the problem — a run
/// is assumed to have crashed unless it explicitly says otherwise, so the *next* start can
/// report a death that the dying process itself never got to describe, together with the last
/// lifecycle event it saw (which is how a crash gets tied to a suspend/resume).
/// </para>
/// <para>
/// The file is plain <c>key=value</c> text rather than JSON so it can be read by a human in a
/// support conversation, and so a partially written file degrades to "unparseable" instead of
/// throwing.
/// </para>
/// </summary>
public sealed class AppSessionMarker
{
    /// <summary>Marker written before the app has recorded anything more specific.</summary>
    public const string StartupEvent = "startup";

    private const string FileName = "session.txt";
    private const string TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffzzz";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _path;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private readonly int _processId = Environment.ProcessId;
    private readonly string _appVersion;
    private readonly DateTimeOffset _startedAt;

    private DateTimeOffset _lastHeartbeat;
    private string _lastEvent = StartupEvent;
    private bool _cleanExit;
    private bool _crashRecorded;
    private string _exitReason = string.Empty;

    /// <summary>
    /// State left behind by the previous run, or null when there is none (first start) or the
    /// file could not be parsed. Captured before this run overwrites it.
    /// </summary>
    public AppSessionSnapshot? PreviousSession { get; }

    /// <param name="directory">Directory holding the marker (the log directory).</param>
    /// <param name="clock">Clock used for timestamps (injected for tests).</param>
    /// <param name="appVersion">Build identity to stamp into the marker.</param>
    public AppSessionMarker(string directory, TimeProvider clock, string appVersion)
    {
        _clock = clock;
        _appVersion = appVersion;
        _path = Path.Combine(directory, FileName);

        try
        {
            Directory.CreateDirectory(directory);
        }
        catch
        {
            // Persist() will fail too and is already tolerant of that.
        }

        PreviousSession = TryRead(_path);

        _startedAt = clock.GetLocalNow();
        _lastHeartbeat = _startedAt;
        Persist();
    }

    /// <summary>
    /// True when the previous run never reached an orderly shutdown — i.e. it was killed, or it
    /// died in a way that left no exception behind.
    /// </summary>
    public bool PreviousSessionCrashed => PreviousSession is { CleanExit: false };

    /// <summary>Refreshes the liveness timestamp without changing the last recorded event.</summary>
    public void Heartbeat()
    {
        lock (_gate)
        {
            _lastHeartbeat = _clock.GetLocalNow();
            Persist();
        }
    }

    /// <summary>
    /// Records the most recent lifecycle event (<c>suspend</c>, <c>resume</c>, <c>display-off</c>, …).
    /// If the process dies next, this is the breadcrumb that says what it was doing.
    /// </summary>
    public void RecordEvent(string name)
    {
        lock (_gate)
        {
            _lastEvent = name;
            _lastHeartbeat = _clock.GetLocalNow();
            Persist();
        }
    }

    /// <summary>
    /// Marks the run as having shut down on purpose, so nothing is reported on the next start.
    /// Ignored once <see cref="MarkCrashed"/> has run: a fatal error is followed by an orderly
    /// shutdown, and letting that shutdown overwrite the record would erase the crash.
    /// </summary>
    public void MarkCleanExit(string reason)
    {
        lock (_gate)
        {
            if (_crashRecorded)
                return;

            _cleanExit = true;
            _exitReason = reason;
            _lastHeartbeat = _clock.GetLocalNow();
            Persist();
        }
    }

    /// <summary>
    /// Records a known-fatal condition before the process goes down, so the next start can name
    /// the cause instead of only reporting "did not exit cleanly".
    /// </summary>
    public void MarkCrashed(string reason)
    {
        lock (_gate)
        {
            // Keep the first reason: the original fault explains the ones that follow it.
            if (_crashRecorded)
                return;

            _crashRecorded = true;
            _cleanExit = false;
            _exitReason = reason;
            _lastHeartbeat = _clock.GetLocalNow();
            Persist();
        }
    }

    private void Persist()
    {
        try
        {
            var builder = new StringBuilder();
            Append(builder, "pid", _processId.ToString(CultureInfo.InvariantCulture));
            Append(builder, "app", _appVersion);
            Append(builder, "startedAt", _startedAt.ToString(TimestampFormat, CultureInfo.InvariantCulture));
            Append(builder, "lastHeartbeat", _lastHeartbeat.ToString(TimestampFormat, CultureInfo.InvariantCulture));
            Append(builder, "lastEvent", _lastEvent);
            Append(builder, "cleanExit", _cleanExit ? "true" : "false");
            Append(builder, "exitReason", _exitReason);

            File.WriteAllText(_path, builder.ToString(), Utf8NoBom);
        }
        catch
        {
            // Same contract as the log sinks: diagnostics never take the app down.
        }
    }

    private static void Append(StringBuilder builder, string key, string value)
        // Newlines would be read back as extra keys, so flatten them into the value.
        => builder.Append(key).Append('=')
                  .Append(value.Replace("\r", " ").Replace("\n", " "))
                  .Append('\n');

    private static AppSessionSnapshot? TryRead(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var line in File.ReadAllLines(path))
            {
                var separator = line.IndexOf('=');
                if (separator > 0)
                    values[line[..separator]] = line[(separator + 1)..];
            }

            if (!values.TryGetValue("startedAt", out var startedRaw)
                || !TryParseTimestamp(startedRaw, out var startedAt))
            {
                return null;
            }

            // A marker without a heartbeat is still useful; fall back to the start time.
            if (!values.TryGetValue("lastHeartbeat", out var heartbeatRaw)
                || !TryParseTimestamp(heartbeatRaw, out var lastHeartbeat))
            {
                lastHeartbeat = startedAt;
            }

            _ = int.TryParse(
                values.GetValueOrDefault("pid"), NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var processId);

            return new AppSessionSnapshot(
                processId,
                values.GetValueOrDefault("app", "unknown"),
                startedAt,
                lastHeartbeat,
                values.GetValueOrDefault("lastEvent", "unknown"),
                string.Equals(values.GetValueOrDefault("cleanExit"), "true", StringComparison.Ordinal),
                values.GetValueOrDefault("exitReason", string.Empty));
        }
        catch
        {
            // A truncated or unreadable marker is treated as "no information", never as a failure.
            return null;
        }
    }

    private static bool TryParseTimestamp(string value, out DateTimeOffset parsed)
        => DateTimeOffset.TryParseExact(
            value, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed);
}
