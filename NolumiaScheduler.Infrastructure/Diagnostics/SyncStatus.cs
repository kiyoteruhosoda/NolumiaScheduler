using NolumiaScheduler.Infrastructure.ExternalCalendars;
using NolumiaScheduler.Infrastructure.Server;

namespace NolumiaScheduler.Infrastructure.Diagnostics;

/// <summary>Something in <see cref="SyncStatus"/> that explains why Outlook or server events may not show.</summary>
public enum SyncProblem
{
    /// <summary>Not signed in: nothing is sent or read.</summary>
    NotSignedIn,
    /// <summary>This PC's events were never moved; the app still reads the local files, not the server.</summary>
    NotMovedToServer,
    /// <summary><c>external-calendar.json</c> has <c>enabled: false</c>.</summary>
    OutlookDisabled,
    /// <summary>The Power Automate file is not where <c>external-calendar.json</c> points.</summary>
    OutlookFileMissing,
    /// <summary>The file has not been rewritten for a day: Power Automate (or OneDrive) has stopped.</summary>
    OutlookFileOld,
    /// <summary>The file changed after the last successful send and no send has caught up yet.</summary>
    OutlookFileNotSentYet,
    /// <summary>The latest send did not go through.</summary>
    OutlookSendFailed,
    /// <summary>The latest read of the server failed; the app shows its saved copy.</summary>
    ServerReadFailed,
    /// <summary>The copy is older than a few refresh rounds.</summary>
    ServerCopyOld,
    /// <summary>The Outlook calendar exists on the server but the copy holds none of its events.</summary>
    OutlookNotInCopy,
    /// <summary>Some events from the server could not be read and are not shown.</summary>
    ItemsSkipped,
}

/// <summary>An Outlook event as the app's copy holds it.</summary>
public sealed record OutlookOccurrence(DateTimeOffset Start, int DurationMinutes, bool IsAllDay, string Title);

/// <summary>Where the inputs of <see cref="SyncStatus.Collect"/> come from (the WinUI shell fills it from its services).</summary>
public sealed record SyncStatusSources(
    TimeProvider Clock,
    string ServerHost,
    bool SignedIn,
    string? UserName,
    DateTimeOffset? MovedAt,
    ServerCalendarStore? Store,
    ExternalCalendarConfig Outlook,
    long? OutlookCalendarId,
    SyncActivity Activity,
    string LogFile);

/// <summary>
/// Everything the sync status window shows: the server sign-in and copy, the Outlook file and its
/// sends, and the <see cref="Problems"/> those add up to. Times are UTC; the window writes them with
/// <see cref="DiagnosticTime"/> in <see cref="Zone"/>.
/// </summary>
public sealed record SyncStatus(
    DateTimeOffset Now,
    TimeZoneInfo Zone,
    string ServerHost,
    bool SignedIn,
    string? UserName,
    DateTimeOffset? MovedAt,
    DateTimeOffset? FetchedAt,
    int EventCount,
    int ImportedCount,
    int OutlookCount,
    DateTimeOffset? OutlookFirst,
    DateTimeOffset? OutlookLast,
    IReadOnlyList<OutlookOccurrence> OutlookToday,
    IReadOnlyList<string> Skipped,
    bool OutlookEnabled,
    string OutlookFilePath,
    int OutlookIntervalMinutes,
    long? OutlookCalendarId,
    bool OutlookFileExists,
    DateTimeOffset? OutlookFileWrittenAt,
    long? OutlookFileSize,
    SyncAttempt? LastOutlookSend,
    SyncAttempt? LastOutlookSent,
    SyncAttempt? LastServerRead,
    SyncAttempt? LastServerReadOk,
    IReadOnlyList<string> RecentLog)
{
    /// <summary>The file is rewritten by Power Automate several times a day; a day without it means it stopped.</summary>
    public static readonly TimeSpan OutlookFileOldAfter = TimeSpan.FromHours(24);

    /// <summary>Three missed rounds of <c>ServerRefreshHost</c> (every 5 minutes).</summary>
    public static readonly TimeSpan ServerCopyOldAfter = TimeSpan.FromMinutes(15);

    /// <summary>A change is sent within 5 seconds of the file changing; allow for a slow OneDrive.</summary>
    public static readonly TimeSpan SendGrace = TimeSpan.FromMinutes(2);

    private const int RecentLogRecords = 40;

    private static readonly string[] LogCategories = [AppLogCategories.ExternalCalendar];

    public IReadOnlyList<SyncProblem> Problems => Diagnose(this);

    public static SyncStatus Collect(SyncStatusSources sources)
    {
        var now = sources.Clock.GetUtcNow();
        var zone = sources.Clock.LocalTimeZone;
        var snapshot = sources.Store?.Snapshot ?? ServerSnapshot.Empty;

        var outlook = sources.OutlookCalendarId is { } calendarId
            ? snapshot.ImportedOccurrences.Where(o => o.CalendarId == calendarId).OrderBy(o => o.Start).ToList()
            : [];
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var outlookToday = outlook
            .Where(o => DateOf(o, zone) == today)
            .Select(o => new OutlookOccurrence(o.Start, o.DurationMinutes, o.IsAllDay, o.Title))
            .ToList();

        var path = sources.Outlook.FilePath;
        var file = ReadFile(path);

        return new SyncStatus(
            now, zone,
            sources.ServerHost, sources.SignedIn, sources.UserName, sources.MovedAt,
            snapshot.FetchedAt,
            snapshot.Events.Count, snapshot.ImportedOccurrences.Count,
            outlook.Count, outlook.FirstOrDefault()?.Start, outlook.LastOrDefault()?.Start, outlookToday,
            sources.Store?.Skipped ?? [],
            sources.Outlook.Enabled, path, sources.Outlook.IntervalMinutes, sources.OutlookCalendarId,
            file.Exists, file.WrittenAt, file.Size,
            sources.Activity.LastOutlookSend, sources.Activity.LastOutlookSent,
            sources.Activity.LastServerRead, sources.Activity.LastServerReadOk,
            AppLogTail.Read(sources.LogFile, LogCategories, RecentLogRecords));
    }

    /// <summary>The problems in the order a person should look at them (the first one usually explains the rest).</summary>
    public static IReadOnlyList<SyncProblem> Diagnose(SyncStatus s)
    {
        var problems = new List<SyncProblem>();
        if (!s.SignedIn) problems.Add(SyncProblem.NotSignedIn);
        if (s.MovedAt is null) problems.Add(SyncProblem.NotMovedToServer);

        if (!s.OutlookEnabled)
        {
            problems.Add(SyncProblem.OutlookDisabled);
        }
        else if (!s.OutlookFileExists)
        {
            problems.Add(SyncProblem.OutlookFileMissing);
        }
        else
        {
            if (s.OutlookFileWrittenAt is { } written && s.Now - written > OutlookFileOldAfter)
                problems.Add(SyncProblem.OutlookFileOld);
            if (s.LastOutlookSend is { Succeeded: false })
                problems.Add(SyncProblem.OutlookSendFailed);
            else if (s.OutlookFileWrittenAt is { } changed
                     && (s.LastOutlookSent is null || s.LastOutlookSent.At < changed)
                     && s.Now - changed > SendGrace)
                problems.Add(SyncProblem.OutlookFileNotSentYet);
        }

        if (s.MovedAt is not null)
        {
            if (s.LastServerRead is { Succeeded: false }) problems.Add(SyncProblem.ServerReadFailed);
            if (s.FetchedAt is null || s.Now - s.FetchedAt.Value > ServerCopyOldAfter) problems.Add(SyncProblem.ServerCopyOld);
            if (s.OutlookCalendarId is not null && s.FetchedAt is not null && s.OutlookCount == 0)
                problems.Add(SyncProblem.OutlookNotInCopy);
        }

        if (s.Skipped.Count > 0) problems.Add(SyncProblem.ItemsSkipped);
        return problems;
    }

    // An all-day occurrence is a date, whatever offset it was sent with; a timed one is on the PC's day.
    private static DateOnly DateOf(ImportedOccurrenceDto o, TimeZoneInfo zone)
        => o.IsAllDay
            ? DateOnly.FromDateTime(o.Start.DateTime)
            : DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(o.Start, zone).DateTime);

    private static (bool Exists, DateTimeOffset? WrittenAt, long? Size) ReadFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists
                ? (true, new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero), info.Length)
                : (false, null, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return (false, null, null);
        }
    }
}
