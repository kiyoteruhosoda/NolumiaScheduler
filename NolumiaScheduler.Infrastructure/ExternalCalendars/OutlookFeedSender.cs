using System.Net;
using System.Text.Json;
using NolumiaScheduler.Infrastructure.Server;

namespace NolumiaScheduler.Infrastructure.ExternalCalendars;

public enum OutlookSendOutcome
{
    /// <summary>The file was sent and the server replaced the Outlook calendar's occurrences.</summary>
    Sent,
    /// <summary>No file yet (Power Automate has not written it); nothing was sent.</summary>
    NoFile,
    /// <summary>The file is being rewritten or is not JSON yet; the next round sends it.</summary>
    Unreadable,
}

/// <summary>
/// Sends the Outlook JSON written by Power Automate to the server as it is (server ADR-0054
/// decision 6: the server reads it and keeps the occurrences as a read-only imported calendar).
/// The first send creates the "Outlook" calendar (work, pushed from the PC) on the server and
/// remembers its id as <c>serverCalendarId</c> in <c>external-calendar.json</c>. Nothing is queued:
/// the feed is a whole snapshot, so a failed round is simply sent again on the next one.
/// </summary>
public sealed class OutlookFeedSender(SchedulerApiClient api, string dataDirectory, string filePath)
{
    public const string CalendarName = "Outlook";
    private const int MaxReadAttempts = 3;

    private long? _calendarId = ExternalCalendarConfig.Load(dataDirectory).ServerCalendarId;

    /// <summary>The server calendar the feed goes to (null until the first successful send).</summary>
    public long? CalendarId => _calendarId;

    /// <summary>
    /// One round. Throws <see cref="ServerException"/> when the server cannot be reached or refuses
    /// (not signed in, file too large, unreadable feed) — the caller logs it and tries next round.
    /// </summary>
    public async Task<OutlookSendOutcome> SendAsync(CancellationToken ct)
    {
        if (!File.Exists(filePath)) return OutlookSendOutcome.NoFile;
        var json = await ReadCompleteJsonAsync(ct).ConfigureAwait(false);
        if (json is null) return OutlookSendOutcome.Unreadable;

        var calendarId = _calendarId ?? await CreateCalendarAsync(ct).ConfigureAwait(false);
        try
        {
            await api.PutOutlookFeedAsync(calendarId, json, ct).ConfigureAwait(false);
        }
        catch (ServerException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Conflict)
        {
            // The remembered calendar was deleted on the server (404) or is not a "sent from the PC"
            // calendar (409). Make a new one and send once more.
            Remember(null);
            calendarId = await CreateCalendarAsync(ct).ConfigureAwait(false);
            await api.PutOutlookFeedAsync(calendarId, json, ct).ConfigureAwait(false);
        }
        return OutlookSendOutcome.Sent;
    }

    private async Task<long> CreateCalendarAsync(CancellationToken ct)
    {
        var created = await api.CreateImportedCalendarAsync(
            new ImportedCalendarCreateDto(CalendarName, "DEFAULT", "WORK", new FeedSourceDto("OUTLOOK_PUSH")), ct)
            .ConfigureAwait(false);
        Remember(created.Id);
        return created.Id;
    }

    private void Remember(long? calendarId)
    {
        _calendarId = calendarId;
        ExternalCalendarConfig.SaveServerCalendarId(dataDirectory, calendarId);
    }

    // OneDrive may be replacing the file while it is read: only whole JSON is sent (the content
    // itself is the server's to interpret), retrying briefly on a partial read.
    private async Task<byte[]?> ReadCompleteJsonAsync(CancellationToken ct)
    {
        for (var attempt = 1; attempt <= MaxReadAttempts; attempt++)
        {
            try
            {
                byte[] bytes;
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var buffer = new MemoryStream())
                {
                    await stream.CopyToAsync(buffer, ct).ConfigureAwait(false);
                    bytes = buffer.ToArray();
                }
                // Parsing from a stream also accepts a UTF-8 BOM, which OneDrive-written files may carry.
                using var _ = JsonDocument.Parse(new MemoryStream(bytes, writable: false));
                return bytes;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                if (attempt == MaxReadAttempts) return null;
                await Task.Delay(500 * attempt, ct).ConfigureAwait(false);
            }
        }
        return null;
    }
}
