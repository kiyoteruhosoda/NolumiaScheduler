using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using NolumiaScheduler.Infrastructure.Auth;

namespace NolumiaScheduler.Infrastructure.Server;

/// <summary>
/// The Scheduler server endpoints the app uses, all called with <c>Authorization: Bearer</c>
/// (server ADR-0054 decision 7). A 401 is retried once with a fresh token; any other non-2xx
/// becomes a <see cref="ServerException"/> carrying the status and the server's <c>reason</c>.
/// </summary>
public sealed class SchedulerApiClient
{
    private readonly HttpClient _http;
    private readonly IAccessTokenSource _tokens;

    public SchedulerApiClient(HttpClient http, Uri baseUri, IAccessTokenSource tokens)
    {
        _http = http;
        BaseUri = baseUri.AbsoluteUri.EndsWith('/') ? baseUri : new Uri(baseUri.AbsoluteUri + "/");
        _tokens = tokens;
    }

    public Uri BaseUri { get; }

    // ── Events ─────────────────────────────────────────────────────────────

    public Task<List<EventDto>> ListEventsAsync(DateOnly from, DateOnly to, CancellationToken ct)
        => GetAsync<List<EventDto>>($"api/calendar/events?from={Day(from)}&to={Day(to)}", ct);

    public Task<EventDto> CreateEventAsync(EventCreateDto body, CancellationToken ct)
        => SendJsonAsync<EventDto>(HttpMethod.Post, "api/calendar/events", body, ct);

    public Task<EventDto> UpdateEventAsync(long id, EventUpdateDto body, CancellationToken ct)
        => SendJsonAsync<EventDto>(HttpMethod.Put, $"api/calendar/events/{id}", body, ct);

    public Task<EventDto> UpdateSeriesAsync(long id, SeriesUpdateDto body, CancellationToken ct)
        => SendJsonAsync<EventDto>(HttpMethod.Put, $"api/calendar/events/{id}/series", body, ct);

    public async Task DeleteEventAsync(long id, CancellationToken ct)
    {
        using var _ = await SendAsync(() => new HttpRequestMessage(HttpMethod.Delete, Url($"api/calendar/events/{id}")), ct)
            .ConfigureAwait(false);
    }

    public Task<EventDto> SkipOccurrenceAsync(long id, OccurrenceActionDto body, CancellationToken ct)
        => SendJsonAsync<EventDto>(HttpMethod.Post, $"api/calendar/events/{id}/occurrences/skip", body, ct);

    public Task<EventDto> RestoreOccurrenceAsync(long id, OccurrenceActionDto body, CancellationToken ct)
        => SendJsonAsync<EventDto>(HttpMethod.Post, $"api/calendar/events/{id}/occurrences/restore", body, ct);

    public Task<EventDto> MoveOccurrenceAsync(long id, OccurrenceMoveDto body, CancellationToken ct)
        => SendJsonAsync<EventDto>(HttpMethod.Post, $"api/calendar/events/{id}/occurrences/move", body, ct);

    public Task<EventDto> CancelMoveOccurrenceAsync(long id, OccurrenceActionDto body, CancellationToken ct)
        => SendJsonAsync<EventDto>(HttpMethod.Post, $"api/calendar/events/{id}/occurrences/cancel-move", body, ct);

    /// <summary>One-time move of the PC's events (decision 8). Same <c>client_ref</c> twice returns the existing id.</summary>
    public Task<EventImportResponseDto> ImportEventsAsync(EventImportRequestDto body, CancellationToken ct)
        => SendJsonAsync<EventImportResponseDto>(HttpMethod.Post, "api/calendar/events/import", body, ct);

    // ── Calendars ──────────────────────────────────────────────────────────

    public Task<List<CalendarDto>> ListCalendarsAsync(CancellationToken ct)
        => GetAsync<List<CalendarDto>>("api/calendars", ct);

    public Task<CalendarDto> CreateCalendarAsync(CalendarCreateDto body, CancellationToken ct)
        => SendJsonAsync<CalendarDto>(HttpMethod.Post, "api/calendars", body, ct);

    public Task<CalendarDto> CreateImportedCalendarAsync(ImportedCalendarCreateDto body, CancellationToken ct)
        => SendJsonAsync<CalendarDto>(HttpMethod.Post, "api/calendars/imported", body, ct);

    public Task<List<DayOffMarkDto>> ListDayOffMarksAsync(DateOnly from, DateOnly to, CancellationToken ct)
        => GetAsync<List<DayOffMarkDto>>($"api/calendars/days-off?from={Day(from)}&to={Day(to)}", ct);

    public Task<List<ImportedOccurrenceDto>> ListImportedOccurrencesAsync(DateOnly from, DateOnly to, CancellationToken ct)
        => GetAsync<List<ImportedOccurrenceDto>>($"api/calendars/imported-occurrences?from={Day(from)}&to={Day(to)}", ct);

    /// <summary>
    /// Sends the Power Automate JSON exactly as it is on disk (decision 6). The server parses it and
    /// replaces the calendar's occurrences wholesale, so sending the same bytes twice is harmless.
    /// </summary>
    public async Task PutOutlookFeedAsync(long calendarId, byte[] json, CancellationToken ct)
    {
        using var _ = await SendAsync(() =>
        {
            var content = new ByteArrayContent(json);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            return new HttpRequestMessage(HttpMethod.Put, Url($"api/calendars/{calendarId}/outlook-feed")) { Content = content };
        }, ct).ConfigureAwait(false);
    }

    // ── Plumbing ───────────────────────────────────────────────────────────

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private Uri Url(string path) => new(BaseUri, path);

    private async Task<T> GetAsync<T>(string path, CancellationToken ct)
    {
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, Url(path)), ct).ConfigureAwait(false);
        return await ReadAsync<T>(response, ct).ConfigureAwait(false);
    }

    private async Task<T> SendJsonAsync<T>(HttpMethod method, string path, object body, CancellationToken ct)
    {
        using var response = await SendAsync(
            () => new HttpRequestMessage(method, Url(path))
            {
                Content = JsonContent.Create(body, body.GetType(), options: ServerJson.Options),
            },
            ct).ConfigureAwait(false);
        return await ReadAsync<T>(response, ct).ConfigureAwait(false);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(ServerJson.Options, ct).ConfigureAwait(false)
                ?? throw new ServerException("The Scheduler server sent an empty response.", response.StatusCode);
        }
        catch (JsonException ex)
        {
            throw new ServerException("The Scheduler server's response could not be read.", response.StatusCode, null, ex);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> createRequest, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var token = await _tokens.GetAccessTokenAsync(ct).ConfigureAwait(false)
                ?? throw new ServerNotSignedInException();

            using var request = createRequest();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                throw new ServerException("Could not reach the Scheduler server.", null, null, ex);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                throw new ServerException("The Scheduler server did not answer in time.", null, null, ex);
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                response.Dispose();
                _tokens.Invalidate(token);
                if (attempt == 0) continue;
                throw new ServerNotSignedInException();
            }

            if (!response.IsSuccessStatusCode)
            {
                var status = response.StatusCode;
                var (reason, detail) = await ReadErrorAsync(response, ct).ConfigureAwait(false);
                response.Dispose();
                var suffix = detail is null ? "" : $": {detail}";
                throw new ServerException($"The Scheduler server refused the request ({(int)status}{suffix}).", status, reason);
            }

            return response;
        }
    }

    // FastAPI errors are {"detail": "..."} or {"detail": {"reason": "...", "detail": "..."}}; a
    // top-level "reason" is also accepted. Anything unreadable just yields no reason.
    private static async Task<(string? Reason, string? Detail)> ReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (null, null);
            string? reason = root.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null;
            string? detail = null;
            if (root.TryGetProperty("detail", out var d))
            {
                if (d.ValueKind == JsonValueKind.String) detail = d.GetString();
                else if (d.ValueKind == JsonValueKind.Object)
                {
                    if (d.TryGetProperty("reason", out var dr) && dr.ValueKind == JsonValueKind.String) reason ??= dr.GetString();
                    if (d.TryGetProperty("detail", out var dd) && dd.ValueKind == JsonValueKind.String) detail = dd.GetString();
                }
            }
            return (reason, detail);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }
}
