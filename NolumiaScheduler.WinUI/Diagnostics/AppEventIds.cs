using NolumiaScheduler.Infrastructure.Diagnostics;

namespace NolumiaScheduler.WinUI.Diagnostics;

/// <summary>
/// Stable Windows event IDs per log category. These are a public contract with anyone who
/// builds an Event Viewer filter or a monitoring rule, so existing values must never be
/// reused for a different meaning — only new ones added.
/// </summary>
internal static class AppEventIds
{
    public const uint Crash = 1000;
    public const uint Lifecycle = 1010;
    public const uint Session = 1020;
    public const uint Power = 1030;
    public const uint Health = 1040;
    public const uint Alarm = 1050;
    public const uint Tray = 1060;
    public const uint Storage = 1070;
    public const uint Other = 1900;

    public static uint For(string category) => category switch
    {
        AppLogCategories.Crash => Crash,
        AppLogCategories.Lifecycle => Lifecycle,
        AppLogCategories.Session => Session,
        AppLogCategories.Power => Power,
        AppLogCategories.Health => Health,
        AppLogCategories.Alarm => Alarm,
        AppLogCategories.Tray => Tray,
        AppLogCategories.Storage => Storage,
        _ => Other,
    };
}
