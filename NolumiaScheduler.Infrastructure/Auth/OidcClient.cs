using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NolumiaScheduler.Infrastructure.Auth;

/// <summary>ログインが失敗した（画面にそのまま出す）。</summary>
public sealed class SignInException(string message, Exception? innerException = null) : Exception(message, innerException);

/// <summary>トークンの口の応答。</summary>
public sealed record TokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken,
    [property: JsonPropertyName("expires_in")] int? ExpiresIn,
    [property: JsonPropertyName("id_token")] string? IdToken);

internal sealed record DiscoveryDocument(
    [property: JsonPropertyName("authorization_endpoint")] Uri AuthorizationEndpoint,
    [property: JsonPropertyName("token_endpoint")] Uri TokenEndpoint);

/// <summary>assay（OIDC）とのやり取り。認可コード＋PKCE（S256）・state・nonce。</summary>
public sealed class OidcClient(HttpClient http, OidcSettings settings)
{
    private DiscoveryDocument? _discovery;

    public OidcSettings Settings => settings;

    /// <summary>ログイン画面の URL。</summary>
    public async Task<Uri> AuthorizeUriAsync(string state, string nonce, string codeChallenge, CancellationToken cancellationToken)
    {
        var discovery = await DiscoverAsync(cancellationToken).ConfigureAwait(false);
        var query = string.Join('&', new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = settings.ClientId,
            ["redirect_uri"] = settings.RedirectUri.AbsoluteUri,
            ["scope"] = settings.Scope,
            ["state"] = state,
            ["nonce"] = nonce,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
        }.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
        var endpoint = discovery.AuthorizationEndpoint.AbsoluteUri;
        return new Uri(endpoint + (endpoint.Contains('?', StringComparison.Ordinal) ? "&" : "?") + query);
    }

    public async Task<TokenResponse> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken cancellationToken) =>
        await PostTokenAsync(
            new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = settings.RedirectUri.AbsoluteUri,
                ["client_id"] = settings.ClientId,
                ["code_verifier"] = codeVerifier,
            },
            cancellationToken).ConfigureAwait(false);

    public async Task<TokenResponse> RefreshAsync(string refreshToken, CancellationToken cancellationToken) =>
        await PostTokenAsync(
            new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = settings.ClientId,
            },
            cancellationToken).ConfigureAwait(false);

    /// <summary>ID トークンの中身（署名は確かめない。トークンの口から TLS で直接受け取ったもののため）。</summary>
    public static IReadOnlyDictionary<string, JsonElement> ReadIdTokenClaims(string idToken)
    {
        var parts = idToken.Split('.');
        if (parts.Length < 2)
        {
            throw new SignInException("ID トークンの形が読めません。");
        }

        try
        {
            var json = Encoding.UTF8.GetString(Pkce.FromBase64Url(parts[1]));
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)
                ?? throw new SignInException("ID トークンが空です。");
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            throw new SignInException("ID トークンが読めません。", exception);
        }
    }

    private async Task<DiscoveryDocument> DiscoverAsync(CancellationToken cancellationToken)
    {
        if (_discovery is not null)
        {
            return _discovery;
        }

        try
        {
            _discovery = await http.GetFromJsonAsync<DiscoveryDocument>(settings.DiscoveryUri, cancellationToken).ConfigureAwait(false)
                ?? throw new SignInException("ログインの設定（discovery）が空でした。");
            return _discovery;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            throw new SignInException("ログインの仕組み（assay）につながりません。", exception);
        }
    }

    private async Task<TokenResponse> PostTokenAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        var discovery = await DiscoverAsync(cancellationToken).ConfigureAwait(false);
        HttpResponseMessage response;
        try
        {
            using var content = new FormUrlEncodedContent(form);
            response = await http.PostAsync(discovery.TokenEndpoint, content, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new TokenEndpointException(null, "ログインの仕組み（assay）につながりません。", exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var error = await ReadErrorAsync(response, cancellationToken).ConfigureAwait(false);
                throw new TokenEndpointException(error, $"ログインの仕組みに断られました（{(int)response.StatusCode} {error}）。");
            }

            try
            {
                return await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken).ConfigureAwait(false)
                    ?? throw new TokenEndpointException(null, "トークンの応答が空でした。");
            }
            catch (JsonException exception)
            {
                throw new TokenEndpointException(null, "トークンの応答が読めませんでした。", exception);
            }
        }
    }

    private static async Task<string?> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            return document.RootElement.TryGetProperty("error", out var error) ? error.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>トークンの口が失敗した。<see cref="Error"/> は OAuth の <c>error</c>（<c>invalid_grant</c> など）。</summary>
public sealed class TokenEndpointException(string? error, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string? Error { get; } = error;

    /// <summary>更新トークンが使えない（取り消された・切れた）。もう一度ログインが要る。</summary>
    public bool NeedsSignIn => Error is "invalid_grant" or "invalid_client" or "unauthorized_client";
}
