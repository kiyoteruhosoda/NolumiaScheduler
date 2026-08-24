namespace NolumiaScheduler.Infrastructure.Diagnostics;

/// <summary>
/// Measures how long the UI thread has been failing to answer, by posting a ping to it and
/// timing the reply.
/// <para>
/// The lag is measured from the moment the *unanswered* ping was sent, and is zero while
/// nothing is outstanding. Measuring it from the last reply instead is what made every healthy
/// sample report a full sample interval: the reply lands a moment after a sample is taken, so
/// the next sample is always one interval later than it, and a perfectly responsive UI read as
/// permanently stalled.
/// </para>
/// <para>
/// Lives here rather than beside the dispatcher so it can be tested without a UI thread: the
/// caller supplies the enqueue and the tick source.
/// </para>
/// </summary>
public sealed class UiResponsivenessProbe
{
    private readonly Func<Action, bool> _enqueue;
    private readonly Func<long> _tickSource;

    private long _pingSentTicks;
    private int _pingPending;

    /// <param name="enqueue">
    /// Posts the reply callback to the UI thread; returns false when it could not be queued.
    /// </param>
    /// <param name="tickSource">
    /// Monotonic millisecond counter. Defaults to <see cref="Environment.TickCount64"/>, which
    /// does not advance while the machine sleeps — so a suspend cannot masquerade as a stall.
    /// </param>
    public UiResponsivenessProbe(Func<Action, bool> enqueue, Func<long>? tickSource = null)
    {
        _enqueue = enqueue;
        _tickSource = tickSource ?? (() => Environment.TickCount64);
    }

    /// <summary>
    /// Returns the lag on the currently outstanding ping (zero when the UI has answered
    /// everything asked of it), then posts the next ping.
    /// </summary>
    /// <remarks>
    /// A new ping is only posted once the previous one has come back, so while the UI is wedged
    /// the send time stays put and the reported lag grows sample after sample. A stall is
    /// therefore visible from the sample *after* the one that posted the unanswered ping — the
    /// measurement is never early, which is the right way round for a warning.
    /// </remarks>
    public TimeSpan Sample()
    {
        var now = _tickSource();

        var lag = Volatile.Read(ref _pingPending) == 1
            ? TimeSpan.FromMilliseconds(now - Volatile.Read(ref _pingSentTicks))
            : TimeSpan.Zero;

        if (Interlocked.CompareExchange(ref _pingPending, 1, 0) == 0)
        {
            // Written before the ping is posted, so the reply can never clear a pending flag
            // that is still pointing at the previous send time.
            Volatile.Write(ref _pingSentTicks, now);

            if (!_enqueue(Answer))
                Volatile.Write(ref _pingPending, 0);
        }

        return lag;
    }

    private void Answer() => Volatile.Write(ref _pingPending, 0);
}
