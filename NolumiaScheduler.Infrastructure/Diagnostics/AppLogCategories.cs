namespace NolumiaScheduler.Infrastructure.Diagnostics;

/// <summary>
/// The fixed set of log categories. Kept as constants (rather than free-form strings at the
/// call sites) because the Windows Event Log sink maps a category to a stable event ID —
/// filtering "all crashes" in Event Viewer only works if the ID never drifts.
/// </summary>
public static class AppLogCategories
{
    /// <summary>Process/app lifecycle: start, launch, exit.</summary>
    public const string Lifecycle = "Lifecycle";

    /// <summary>Unhandled exceptions and evidence of a previous abnormal termination.</summary>
    public const string Crash = "Crash";

    /// <summary>Suspend/resume, display on/off, lid, battery.</summary>
    public const string Power = "Power";

    /// <summary>Windows session: lock/unlock, connect/disconnect, logoff/shutdown.</summary>
    public const string Session = "Session";

    /// <summary>Periodic health sampling: memory, handles, UI responsiveness.</summary>
    public const string Health = "Health";

    /// <summary>Alarm scheduling and notification presentation.</summary>
    public const string Alarm = "Alarm";

    /// <summary>Notification-area (tray) icon.</summary>
    public const string Tray = "Tray";

    /// <summary>Repository/storage access.</summary>
    public const string Storage = "Storage";
}
