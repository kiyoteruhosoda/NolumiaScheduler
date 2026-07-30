namespace NolumiaScheduler.Infrastructure.Diagnostics;

/// <summary>
/// Ambient access to the process-wide diagnostic log.
/// <para>
/// This is deliberately static rather than injected. The earliest and most important callers —
/// the entry point and the global exception handlers — run before the DI container exists (and,
/// in the crash case, at a moment when resolving anything is unwise). Everything else should
/// still take <see cref="IAppLog"/> through the constructor.
/// </para>
/// </summary>
public static class AppLog
{
    private static IAppLog _current = NullAppLog.Instance;

    /// <summary>The active log. Never null; discards records until <see cref="Initialize"/> runs.</summary>
    public static IAppLog Current => Volatile.Read(ref _current);

    /// <summary>Installs the process-wide sink. Called once, from the entry point.</summary>
    public static void Initialize(IAppLog log) => Volatile.Write(ref _current, log);

    /// <summary>Restores the default no-op sink (used by tests to avoid cross-test leakage).</summary>
    public static void Reset() => Volatile.Write(ref _current, NullAppLog.Instance);
}
