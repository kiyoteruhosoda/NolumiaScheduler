using System.Text.Json;
using NolumiaScheduler.Infrastructure.Auth;

namespace NolumiaScheduler.Infrastructure.Server;

/// <summary>
/// Where the Scheduler server is and which assay client signs in to it, read from <c>server.json</c>
/// in the data directory. Every value defaults to production (scheduler.nolumia.com and the assay client
/// <c>nolumiascheduler-win</c>); the file is only needed to point the app elsewhere, e.g. stg:
/// <code>
/// { "baseUrl": "https://schedulerstg.nolumia.com" }
/// </code>
/// A missing or unreadable file falls back to the defaults; it is never written by the app.
/// </summary>
public sealed record ServerSettings(Uri BaseUri, OidcSettings Oidc)
{
    public const string FileName = "server.json";
    public static readonly Uri DefaultBaseUri = new("https://scheduler.nolumia.com/");

    public static ServerSettings Defaults { get; } = new(
        DefaultBaseUri,
        new OidcSettings(OidcSettings.DefaultIssuer, OidcSettings.DefaultClientId, OidcSettings.DefaultRedirectUri, OidcSettings.DefaultScope));

    public static ServerSettings Load(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, FileName);
        if (!File.Exists(path)) return Defaults;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            return new ServerSettings(
                WithTrailingSlash(UriOr(root, "baseUrl", DefaultBaseUri)),
                new OidcSettings(
                    UriOr(root, "issuer", OidcSettings.DefaultIssuer),
                    StringOr(root, "clientId", OidcSettings.DefaultClientId),
                    UriOr(root, "redirectUri", OidcSettings.DefaultRedirectUri),
                    StringOr(root, "scope", OidcSettings.DefaultScope)));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UriFormatException or InvalidOperationException)
        {
            return Defaults;
        }
    }

    private static string StringOr(JsonElement root, string name, string fallback)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : fallback;

    private static Uri UriOr(JsonElement root, string name, Uri fallback)
        => StringOr(root, name, "") is { Length: > 0 } text ? new Uri(text) : fallback;

    private static Uri WithTrailingSlash(Uri uri)
        => uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");
}
