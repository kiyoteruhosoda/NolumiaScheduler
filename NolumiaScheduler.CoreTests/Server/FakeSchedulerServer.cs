using System.Net;
using System.Text;
using System.Text.Json;
using NolumiaScheduler.Infrastructure.Auth;
using NolumiaScheduler.Infrastructure.Server;

namespace NolumiaScheduler.CoreTests.Server;

/// <summary>A request the fake server received (body already read).</summary>
internal sealed record ReceivedRequest(HttpMethod Method, string PathAndQuery, string? Authorization, string Body)
{
    public JsonElement Json => JsonDocument.Parse(Body).RootElement;
}

/// <summary>
/// Stand-in for the Scheduler server: answers by route with canned or computed JSON and records
/// every request, so tests can assert exactly what the app sent.
/// </summary>
internal sealed class FakeSchedulerServer : HttpMessageHandler
{
    private readonly List<(HttpMethod Method, string Path, Func<ReceivedRequest, (HttpStatusCode, string)> Respond)> _routes = [];

    public List<ReceivedRequest> Requests { get; } = [];

    /// <summary>When set, every request fails as if the network were down.</summary>
    public bool Offline { get; set; }

    public FakeSchedulerServer On(HttpMethod method, string path, Func<ReceivedRequest, (HttpStatusCode, string)> respond)
    {
        _routes.Insert(0, (method, path, respond));
        return this;
    }

    public FakeSchedulerServer On(HttpMethod method, string path, HttpStatusCode status, string json)
        => On(method, path, _ => (status, json));

    public FakeSchedulerServer OnJson(HttpMethod method, string path, object body)
        => On(method, path, _ => (HttpStatusCode.OK, Serialize(body)));

    public static string Serialize(object body) => JsonSerializer.Serialize(body, body.GetType(), ServerJson.Options);

    public IEnumerable<ReceivedRequest> Sent(HttpMethod method, string pathPrefix)
        => Requests.Where(r => r.Method == method && r.PathAndQuery.StartsWith(pathPrefix, StringComparison.Ordinal));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Offline) throw new HttpRequestException("offline");

        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        var received = new ReceivedRequest(
            request.Method, request.RequestUri!.PathAndQuery, request.Headers.Authorization?.ToString(), body);
        Requests.Add(received);

        var path = request.RequestUri.AbsolutePath;
        foreach (var (method, routePath, respond) in _routes)
        {
            if (method != request.Method || routePath != path) continue;
            var (status, json) = respond(received);
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
        return new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("""{"detail":"Not Found"}""", Encoding.UTF8, "application/json"),
        };
    }
}

internal sealed class FixedToken(string? token = "token-1") : IAccessTokenSource
{
    public List<string> Invalidated { get; } = [];
    public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken) => Task.FromResult(token);
    public void Invalidate(string accessToken) => Invalidated.Add(accessToken);
}

/// <summary>Builders for server-shaped events.</summary>
internal static class ServerEvents
{
    public static EventDto Single(long id, string title = "Meeting", int version = 3) => new(
        Id: id,
        Kind: "SINGLE",
        Title: title,
        TimeZone: "Asia/Tokyo",
        Start: new DateTimeOffset(2026, 11, 2, 1, 0, 0, TimeSpan.Zero), // 10:00 Tokyo
        DurationMinutes: 60,
        Recurrence: null,
        Location: "Room A",
        Description: null,
        ColorKey: "DEFAULT",
        TaskId: 77,
        Alarm: new AlarmDto(true, true, true, true, true),
        EventType: "TASK",
        CalendarId: 5,
        Exceptions: [],
        Moves: [],
        Version: version,
        CreatedAt: new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
        UpdatedAt: new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));

    /// <summary>Every Monday and Wednesday 09:00 Tokyo from 2026-11-02, shifted forward over days off.</summary>
    public static EventDto Weekly(long id, int version = 4) => Single(id, "Standup", version) with
    {
        Kind = "RECURRING",
        Start = new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero), // 09:00 Tokyo
        DurationMinutes = 15,
        Recurrence = new RecurrenceRuleDto("WEEKLY", 1, null, new WeeklyRuleDto(["MO", "WE"]), null, null,
            new AdjustmentRuleDto("HOLIDAY", "BUSINESS_DAY", 1, "SHIFT")),
        EventType = "EVENT",
        TaskId = null,
    };
}
