using NolumiaScheduler.Infrastructure.Diagnostics;

namespace NolumiaScheduler.CoreTests;

/// <summary>
/// The probe reports a UI stall, so a false positive here means a warning on every healthy
/// sample — which is exactly the regression these tests exist to prevent.
/// </summary>
[TestClass]
public class UiResponsivenessProbeTests
{
    /// <summary>A stand-in UI thread whose replies the test decides when to deliver.</summary>
    private sealed class FakeUiThread
    {
        private readonly List<Action> _queued = [];

        public bool AcceptsWork { get; set; } = true;
        public bool Enqueue(Action reply)
        {
            if (!AcceptsWork)
                return false;

            _queued.Add(reply);
            return true;
        }

        /// <summary>Runs everything queued — the responsive UI thread.</summary>
        public void RunPending()
        {
            var pending = _queued.ToArray();
            _queued.Clear();
            foreach (var reply in pending)
                reply();
        }
    }

    private long _ticks;
    private FakeUiThread _ui = null!;
    private UiResponsivenessProbe _probe = null!;

    [TestInitialize]
    public void Setup()
    {
        _ticks = 0;
        _ui = new FakeUiThread();
        _probe = new UiResponsivenessProbe(_ui.Enqueue, () => _ticks);
    }

    private TimeSpan SampleAfter(TimeSpan elapsed)
    {
        _ticks += (long)elapsed.TotalMilliseconds;
        return _probe.Sample();
    }

    [TestMethod]
    public void 応答しているUIスレッドは何度標本を取ってもラグ0を返す()
    {
        // The regression: measuring from the last reply instead of the outstanding ping made
        // every one of these report a whole sample interval, i.e. a permanent false stall.
        for (var i = 0; i < 5; i++)
        {
            var lag = SampleAfter(TimeSpan.FromMinutes(1));
            _ui.RunPending();

            Assert.AreEqual(TimeSpan.Zero, lag, $"sample {i + 1} reported a stall on a responsive UI");
        }
    }

    [TestMethod]
    public void 初回の標本は基準がないのでラグ0を返す()
    {
        Assert.AreEqual(TimeSpan.Zero, SampleAfter(TimeSpan.FromMinutes(1)));
    }

    [TestMethod]
    public void 応答しないUIスレッドではラグが標本ごとに伸びる()
    {
        SampleAfter(TimeSpan.FromMinutes(1));   // posts the ping that is never answered

        Assert.AreEqual(TimeSpan.FromMinutes(1), SampleAfter(TimeSpan.FromMinutes(1)));
        Assert.AreEqual(TimeSpan.FromMinutes(2), SampleAfter(TimeSpan.FromMinutes(1)));
        Assert.AreEqual(TimeSpan.FromMinutes(3), SampleAfter(TimeSpan.FromMinutes(1)));
    }

    [TestMethod]
    public void 復帰したUIスレッドは次の標本でラグ0に戻る()
    {
        SampleAfter(TimeSpan.FromMinutes(1));
        Assert.AreEqual(TimeSpan.FromMinutes(2), SampleAfter(TimeSpan.FromMinutes(2)));

        // The UI finally drains its queue.
        _ui.RunPending();

        Assert.AreEqual(TimeSpan.Zero, SampleAfter(TimeSpan.FromMinutes(1)));
    }

    [TestMethod]
    public void スリープをまたいでもラグは増えない()
    {
        SampleAfter(TimeSpan.FromMinutes(1));   // posts the ping
        var beforeSleep = SampleAfter(TimeSpan.FromMinutes(1));

        // The machine sleeps: hours of wall-clock time pass, but Environment.TickCount64 does
        // not advance across a suspend, which is the tick source standing still here. A suspend
        // must not masquerade as the UI having been wedged for that whole time.
        var afterSleep = SampleAfter(TimeSpan.Zero);

        Assert.AreEqual(TimeSpan.FromMinutes(1), beforeSleep);
        Assert.AreEqual(beforeSleep, afterSleep);
    }

    [TestMethod]
    public void キューに積めなかった場合は次の標本で積み直す()
    {
        _ui.AcceptsWork = false;
        SampleAfter(TimeSpan.FromMinutes(1));

        // A ping that never got queued must not be counted as one the UI failed to answer.
        _ui.AcceptsWork = true;
        Assert.AreEqual(TimeSpan.Zero, SampleAfter(TimeSpan.FromMinutes(1)));

        Assert.AreEqual(TimeSpan.FromMinutes(1), SampleAfter(TimeSpan.FromMinutes(1)));
    }
}
