using System.Runtime.InteropServices;
using NolumiaScheduler.Infrastructure.Diagnostics;

namespace NolumiaScheduler.WinUI.Diagnostics;

/// <summary>
/// Writes important records to the Windows <c>Application</c> event log, so a crash is visible
/// in Event Viewer next to the OS's own power/resume entries — the timeline that makes a
/// "it died some time after resume" report diagnosable.
/// <para>
/// This uses the raw <c>advapi32</c> reporting API instead of <c>System.Diagnostics.EventLog</c>
/// to avoid taking a NuGet dependency for what amounts to three P/Invokes, and because the
/// managed wrapper insists on creating the event source (which needs administrator rights).
/// <c>RegisterEventSource</c> with an unregistered source name still logs to the Application
/// log; Event Viewer then prefixes the entry with a "description not found" note because there
/// is no message resource DLL. The full text is still shown, and registering the source once
/// (see <c>docs/diagnostics.md</c>) removes the note.
/// </para>
/// <para>
/// Only <see cref="AppLogLevel.Warning"/> and above are forwarded, plus
/// <see cref="AppLogCategories.Lifecycle"/> at any level. The Application log is a shared,
/// size-capped, machine-wide resource, so routine health sampling stays in the app's own file —
/// but "started" and "shut down" have to be there, because a gap between a start and the next
/// start is what proves the app died rather than being closed.
/// </para>
/// </summary>
internal sealed class WindowsEventLogAppLog : IAppLog
{
    /// <summary>Event source name. Must match the name registered in <c>docs/diagnostics.md</c>.</summary>
    public const string SourceName = "Nolumia Scheduler";

    private const ushort EventLogSuccess = 0x0000;
    private const ushort EventLogErrorType = 0x0001;
    private const ushort EventLogWarningType = 0x0002;
    private const ushort EventLogInformationType = 0x0004;

    // ReportEvent rejects insert strings longer than 31,839 characters and drops the whole
    // event; a long stack trace can get close, so cut well below the limit.
    private const int MaxMessageLength = 30_000;

    private readonly AppLogLevel _minimumLevel;

    public WindowsEventLogAppLog(AppLogLevel minimumLevel = AppLogLevel.Warning)
        => _minimumLevel = minimumLevel;

    public void Write(AppLogLevel level, string category, string message, Exception? exception = null)
    {
        if (!ShouldForward(level, category))
            return;

        try
        {
            var text = $"[{category}] {message}";
            if (exception is not null)
                text += Environment.NewLine + Environment.NewLine + exception;

            if (text.Length > MaxMessageLength)
                text = text[..MaxMessageLength] + "… (truncated)";

            var handle = RegisterEventSourceW(null, SourceName);
            if (handle == nint.Zero)
                return;

            try
            {
                ReportEventW(
                    handle,
                    TypeFor(level),
                    wCategory: 0,
                    AppEventIds.For(category),
                    lpUserSid: nint.Zero,
                    wNumStrings: 1,
                    dwDataSize: 0,
                    [text],
                    lpRawData: nint.Zero);
            }
            finally
            {
                DeregisterEventSource(handle);
            }
        }
        catch
        {
            // Event log access can fail (policy, full log, denied). The file sink still has it.
        }
    }

    private bool ShouldForward(AppLogLevel level, string category)
        => level >= _minimumLevel
           || (category == AppLogCategories.Lifecycle && level >= AppLogLevel.Info);

    private static ushort TypeFor(AppLogLevel level) => level switch
    {
        AppLogLevel.Fatal or AppLogLevel.Error => EventLogErrorType,
        AppLogLevel.Warning => EventLogWarningType,
        AppLogLevel.Info => EventLogInformationType,
        _ => EventLogSuccess,
    };

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint RegisterEventSourceW(string? lpUNCServerName, string lpSourceName);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DeregisterEventSource(nint hEventLog);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ReportEventW(
        nint hEventLog,
        ushort wType,
        ushort wCategory,
        uint dwEventID,
        nint lpUserSid,
        ushort wNumStrings,
        uint dwDataSize,
        [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] lpStrings,
        nint lpRawData);
}
