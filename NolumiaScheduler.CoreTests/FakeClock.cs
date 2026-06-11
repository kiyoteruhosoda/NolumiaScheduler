namespace NolumiaScheduler.CoreTests;

/// <summary>
/// Deterministic TimeProvider for tests: time only moves when the test advances it.
/// LocalTimeZone defaults to UTC so local-time-based logic is reproducible on any machine.
/// </summary>
internal sealed class FakeClock(DateTimeOffset initialUtcNow, TimeZoneInfo? localTimeZone = null) : TimeProvider
{
    private DateTimeOffset _utcNow = initialUtcNow;
    private readonly TimeZoneInfo _localTimeZone = localTimeZone ?? TimeZoneInfo.Utc;

    public override DateTimeOffset GetUtcNow() => _utcNow;
    public override TimeZoneInfo LocalTimeZone => _localTimeZone;

    public void Advance(TimeSpan delta) => _utcNow += delta;
    public void SetUtcNow(DateTimeOffset utcNow) => _utcNow = utcNow;
}
