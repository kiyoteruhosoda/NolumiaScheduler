using System.Globalization;
using System.Text;

namespace NolumiaScheduler.Infrastructure.Diagnostics;

/// <summary>
/// Appends records to a per-day text file (<c>nolumia-yyyyMMdd.log</c>) under a log directory,
/// pruning files older than the retention window.
/// <para>
/// Each record is written with its own open/append/close cycle. That is slower than holding a
/// buffered writer, but the whole point of this log is to survive a process that dies without
/// warning — a buffered tail lost at crash time would hide exactly the lines that matter. The
/// write volume (lifecycle transitions plus one health sample per minute) makes the cost
/// irrelevant.
/// </para>
/// </summary>
public sealed class FileAppLog : IAppLog
{
    private const string FilePrefix = "nolumia-";
    private const string FileExtension = ".log";
    private const string DateStampFormat = "yyyyMMdd";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly object _gate = new();
    private readonly TimeProvider _clock;
    private readonly AppLogLevel _minimumLevel;
    private readonly int _retentionDays;
    private readonly int _processId = Environment.ProcessId;
    private DateOnly _lastPruneDate;

    /// <summary>Directory the log files live in. Created lazily on first write.</summary>
    public string LogDirectory { get; }

    /// <param name="logDirectory">Directory to write log files into.</param>
    /// <param name="clock">Clock used for timestamps and day rollover (injected for tests).</param>
    /// <param name="minimumLevel">Records below this level are dropped.</param>
    /// <param name="retentionDays">Delete log files older than this many days; 0 disables pruning.</param>
    public FileAppLog(
        string logDirectory,
        TimeProvider clock,
        AppLogLevel minimumLevel = AppLogLevel.Info,
        int retentionDays = 14)
    {
        LogDirectory = logDirectory;
        _clock = clock;
        _minimumLevel = minimumLevel;
        _retentionDays = retentionDays;
    }

    /// <summary>Full path of the file the next record would be appended to.</summary>
    public string CurrentFilePath => FilePathFor(_clock.GetLocalNow());

    public void Write(AppLogLevel level, string category, string message, Exception? exception = null)
    {
        if (level < _minimumLevel)
            return;

        try
        {
            var now = _clock.GetLocalNow();
            var record = Format(now, level, category, message, exception);

            lock (_gate)
            {
                Directory.CreateDirectory(LogDirectory);
                PruneOnDayChange(DateOnly.FromDateTime(now.DateTime));

                // FileShare.ReadWrite so the file can be tailed (or opened in an editor) while
                // the app is running — the usual way a user collects it for a bug report.
                using var stream = new FileStream(
                    FilePathFor(now), FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var writer = new StreamWriter(stream, Utf8NoBom);
                writer.Write(record);
            }
        }
        catch
        {
            // Diagnostics must never take the app down. A dropped line is strictly better than
            // an exception thrown out of a crash handler, which would mask the original fault.
        }
    }

    private string Format(
        DateTimeOffset now, AppLogLevel level, string category, string message, Exception? exception)
    {
        var builder = new StringBuilder();
        builder.Append(now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture));
        builder.Append(' ').Append(level.ToString().ToUpperInvariant().PadRight(7));
        builder.Append("pid=").Append(_processId);
        builder.Append(" tid=").Append(Environment.CurrentManagedThreadId);
        builder.Append(" [").Append(category).Append("] ");
        builder.Append(message);
        builder.Append(Environment.NewLine);

        if (exception is not null)
        {
            // Indent the exception block so a line-oriented reader (or grep for the timestamp
            // prefix) can still tell records apart.
            foreach (var line in exception.ToString().Split('\n'))
                builder.Append("    ").Append(line.TrimEnd('\r')).Append(Environment.NewLine);
        }

        return builder.ToString();
    }

    private string FilePathFor(DateTimeOffset now)
        => Path.Combine(
            LogDirectory,
            FilePrefix + now.ToString(DateStampFormat, CultureInfo.InvariantCulture) + FileExtension);

    /// <summary>
    /// Deletes expired log files, at most once per local day. Runs inside the write lock and
    /// swallows its own failures so a locked or unreadable file cannot block logging.
    /// </summary>
    private void PruneOnDayChange(DateOnly today)
    {
        if (_lastPruneDate == today)
            return;

        _lastPruneDate = today;
        if (_retentionDays <= 0)
            return;

        var cutoff = today.AddDays(-_retentionDays);

        try
        {
            foreach (var path in Directory.EnumerateFiles(LogDirectory, FilePrefix + "*" + FileExtension))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                if (name.Length != FilePrefix.Length + DateStampFormat.Length)
                    continue;

                var stamp = name[FilePrefix.Length..];
                if (!DateOnly.TryParseExact(
                        stamp, DateStampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    continue;
                }

                if (date < cutoff)
                {
                    try
                    {
                        File.Delete(path);
                    }
                    catch
                    {
                        // Another process may hold the file; it will be retried tomorrow.
                    }
                }
            }
        }
        catch
        {
            // Enumeration failure (missing/denied directory) is not worth failing a write over.
        }
    }
}
