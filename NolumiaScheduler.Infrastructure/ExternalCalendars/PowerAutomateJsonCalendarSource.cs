using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using NolumiaScheduler.Domain.ExternalCalendars;

namespace NolumiaScheduler.Infrastructure.ExternalCalendars;

/// <summary>
/// Reads the JSON array written by the Power Automate flow (Outlook calendar view).
/// Expected fields per item: id, subject, start, end, isAllDay, location, isReminderOn,
/// reminderMinutes, sensitivity, lastModified. Optional: joinUrl / onlineMeetingUrl / body (Teams link).
/// </summary>
public sealed class PowerAutomateJsonCalendarSource(string sourceId, string filePath) : IExternalCalendarSource
{
    private const int MaxReadAttempts = 3;

    public string SourceId { get; } = sourceId;
    public string FilePath { get; } = filePath;

    public IReadOnlyList<ExternalAppointment>? ReadSnapshot()
    {
        if (!File.Exists(FilePath)) return null;

        // OneDrive may be replacing the file while we read it; retry briefly on partial content.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return Parse(ReadShared(FilePath));
            }
            catch (Exception ex) when (ex is IOException or JsonException && attempt < MaxReadAttempts)
            {
                Thread.Sleep(500 * attempt);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static IReadOnlyList<ExternalAppointment> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("value", out var value))
            root = value;
        if (root.ValueKind != JsonValueKind.Array)
            throw new JsonException("Expected a JSON array of appointments.");

        var result = new List<ExternalAppointment>();
        foreach (var item in root.EnumerateArray())
        {
            var appt = ParseItem(item);
            if (appt != null) result.Add(appt);
        }
        return result;
    }

    private static ExternalAppointment? ParseItem(JsonElement item)
    {
        var id = GetString(item, "id");
        var startText = GetString(item, "start");
        var endText = GetString(item, "end");
        if (string.IsNullOrWhiteSpace(id) || !TryParseInstant(startText, out var start))
            return null;
        if (!TryParseInstant(endText, out var end) || end < start)
            end = start;

        var isAllDay = GetBool(item, "isAllDay") ?? false;
        var reminderOn = GetBool(item, "isReminderOn") ?? false;
        int? reminder = reminderOn ? GetInt(item, "reminderMinutes") : null;
        var sensitivity = GetString(item, "sensitivity");
        DateTimeOffset? lastModified = TryParseInstant(GetString(item, "lastModified"), out var lm) ? lm : null;

        // All-day values arrive as "yyyy-MM-ddT00:00:00+00:00": the date part is the calendar date.
        var startDate = DateOnly.FromDateTime(start.DateTime);
        var endDate = DateOnly.FromDateTime(end.DateTime);

        return new ExternalAppointment(
            id!,
            GetString(item, "subject") ?? "",
            isAllDay,
            start.ToUniversalTime(),
            end.ToUniversalTime(),
            startDate,
            endDate,
            ResolveLocation(item),
            reminder,
            sensitivity is not null && !sensitivity.Equals("normal", StringComparison.OrdinalIgnoreCase),
            lastModified);
    }

    // Teams meetings report a placeholder location ("Microsoft Teams 会議"); prefer the join URL instead.
    private static readonly Regex TeamsJoinUrl = new(
        @"https://teams\.microsoft\.com/l/meetup-join/[^\s""'<>]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static string? ResolveLocation(JsonElement item)
    {
        var location = GetString(item, "location");
        var joinUrl = FindJoinUrl(item);
        if (joinUrl is null) return location;
        if (string.IsNullOrWhiteSpace(location) || IsTeamsPlaceholder(location)) return joinUrl;
        return location;
    }

    private static bool IsTeamsPlaceholder(string location) =>
        location.Contains("Microsoft Teams", StringComparison.OrdinalIgnoreCase);

    private static string? FindJoinUrl(JsonElement item)
    {
        foreach (var name in new[] { "joinUrl", "onlineMeetingUrl" })
        {
            var url = GetString(item, name);
            if (!string.IsNullOrWhiteSpace(url)) return url.Trim();
        }
        if (item.TryGetProperty("onlineMeeting", out var om) && om.ValueKind == JsonValueKind.Object)
        {
            var url = GetString(om, "joinUrl");
            if (!string.IsNullOrWhiteSpace(url)) return url.Trim();
        }
        foreach (var name in new[] { "body", "bodyPreview" })
        {
            var text = GetString(item, name);
            if (string.IsNullOrEmpty(text)) continue;
            var match = TeamsJoinUrl.Match(text);
            if (match.Success) return System.Net.WebUtility.HtmlDecode(match.Value);
        }
        return null;
    }

    private static bool TryParseInstant(string? text, out DateTimeOffset value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        // Graph sometimes omits the offset; such values are UTC.
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces, out value);
    }

    private static string? GetString(JsonElement item, string name) =>
        item.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static bool? GetBool(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var p)) return null;
        return p.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(p.GetString(), out var b) => b,
            _ => null
        };
    }

    private static int? GetInt(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var p)) return null;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var n)) return n;
        if (p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), out n)) return n;
        return null;
    }
}

