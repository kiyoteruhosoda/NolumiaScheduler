using System.Globalization;

namespace NolumiaScheduler.Infrastructure.Diagnostics;

/// <summary>
/// Writes a moment for a person comparing it with other clocks (the server's UTC, Outlook, the log):
/// the PC's local time with its offset spelled out, so nobody has to guess which zone it is in.
/// </summary>
public static class DiagnosticTime
{
    /// <summary><c>2026/10/20 11:14:20 (UTC+09:00)</c> — the moment in <paramref name="zone"/>.</summary>
    public static string Format(DateTimeOffset moment, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(moment, zone);
        return local.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture) + " (" + Offset(local.Offset) + ")";
    }

    /// <summary><c>UTC+09:00</c>, <c>UTC-05:00</c>, <c>UTC</c>.</summary>
    public static string Offset(TimeSpan offset)
    {
        if (offset == TimeSpan.Zero) return "UTC";
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        return $"UTC{sign}{offset.Duration():hh\\:mm}";
    }
}
