using NolumiaScheduler.Infrastructure.Server;

namespace NolumiaScheduler.Infrastructure.Diagnostics;

/// <summary>One round of sending the Outlook feed or reading the server copy, as the sync status window shows it.</summary>
/// <param name="At">When the round finished (UTC).</param>
/// <param name="Reason">What started it: <c>startup</c>, <c>timer</c>, <c>file changed</c>, <c>manual</c>…</param>
/// <param name="Succeeded">False when it did not do its job (not sent, not read).</param>
/// <param name="Result">Short outcome (<c>Sent</c>, <c>NoFile</c>, <c>Read</c>, <c>Failed</c>…).</param>
/// <param name="Error">What went wrong, from <see cref="SyncActivity.Describe"/>; null when it worked.</param>
public sealed record SyncAttempt(DateTimeOffset At, string Reason, bool Succeeded, string Result, string? Error);

/// <summary>
/// Remembers the latest rounds of the two background jobs (Outlook send, server read) in memory, so
/// the sync status window can show what happened without parsing the log. Written from the thread
/// pool and the UI thread; read from the UI thread.
/// </summary>
public sealed class SyncActivity(TimeProvider clock)
{
    private readonly TimeProvider _clock = clock;
    private readonly object _gate = new();
    private SyncAttempt? _lastSend;
    private SyncAttempt? _lastSent;
    private SyncAttempt? _lastRead;
    private SyncAttempt? _lastReadOk;

    /// <summary>The latest attempt to send the Outlook feed, whatever its outcome.</summary>
    public SyncAttempt? LastOutlookSend { get { lock (_gate) return _lastSend; } }

    /// <summary>The latest round that actually sent the Outlook feed.</summary>
    public SyncAttempt? LastOutlookSent { get { lock (_gate) return _lastSent; } }

    /// <summary>The latest attempt to read the server copy, whatever its outcome.</summary>
    public SyncAttempt? LastServerRead { get { lock (_gate) return _lastRead; } }

    /// <summary>The latest round that read the server copy.</summary>
    public SyncAttempt? LastServerReadOk { get { lock (_gate) return _lastReadOk; } }

    public void OutlookSend(string reason, bool succeeded, string result, string? error = null)
    {
        var attempt = new SyncAttempt(_clock.GetUtcNow(), reason, succeeded, result, error);
        lock (_gate)
        {
            _lastSend = attempt;
            if (succeeded) _lastSent = attempt;
        }
    }

    public void ServerRead(string reason, bool succeeded, string? error = null)
    {
        var attempt = new SyncAttempt(_clock.GetUtcNow(), reason, succeeded, succeeded ? "Read" : "Failed", error);
        lock (_gate)
        {
            _lastRead = attempt;
            if (succeeded) _lastReadOk = attempt;
        }
    }

    /// <summary>One line for an exception: the HTTP status and the server's reason when there is one.</summary>
    public static string Describe(Exception exception) => exception switch
    {
        ServerNotSignedInException => "not signed in",
        ServerException { IsUnreachable: true } s =>
            $"server unreachable: {(s.InnerException ?? s).Message}",
        ServerException s =>
            $"HTTP {(int)s.StatusCode!.Value}{(s.Reason is { Length: > 0 } r ? $" {r}" : "")}: {s.Message}",
        _ => $"{exception.GetType().Name}: {exception.Message}",
    };
}
