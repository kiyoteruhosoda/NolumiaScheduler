using System.Text;
using System.Text.Json;
using NolumiaScheduler.Infrastructure.Server;

namespace NolumiaScheduler.Infrastructure.Auth;

/// <summary>
/// Holds the tokens attached to requests to the Scheduler server (server ADR-0054 decision 7; copied from the class of the same name in WinTrack).
/// The refresh token is encrypted with <see cref="ISecretProtector"/> and stored in a file; the access token (300 seconds) is kept only in memory.
/// </summary>
public sealed class SignInSession : IAccessTokenSource, IDisposable
{
    /// <summary>Refresh a little before expiry.</summary>
    private static readonly TimeSpan Margin = TimeSpan.FromSeconds(30);

    private readonly OidcClient _oidc;
    private readonly ISecretProtector _protector;
    private readonly string _file;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _refreshing = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _expiresAt;

    public SignInSession(OidcClient oidc, ISecretProtector protector, string file, TimeProvider? time = null)
    {
        _oidc = oidc;
        _protector = protector;
        _file = file;
        _time = time ?? TimeProvider.System;
        var stored = Load();
        UserName = stored?.UserName;
    }

    /// <summary>Name of the signed-in user (preferred_username etc. from the ID token). Null when not signed in.</summary>
    public string? UserName { get; private set; }

    public bool IsSignedIn => File.Exists(_file);

    /// <summary>The sign-in state changed (signed in, signed out, or expired).</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Signs in. Opens the sign-in page in a browser and receives the result at the redirect URI (127.0.0.1).
    /// </summary>
    /// <param name="openBrowser">Opens the sign-in page in the default browser.</param>
    /// <param name="page">Page shown in the browser on return (in the app language; English if omitted).</param>
    public async Task SignInAsync(Action<Uri> openBrowser, CancellationToken cancellationToken, SignInPage? page = null)
    {
        var state = Pkce.RandomToken();
        var nonce = Pkce.RandomToken();
        var verifier = Pkce.RandomToken();

        IReadOnlyDictionary<string, string> callback;
        LoopbackReceiver receiver;
        try
        {
            receiver = new LoopbackReceiver(_oidc.Settings.RedirectUri, page: page);
        }
        catch (System.Net.Sockets.SocketException exception)
        {
            var authority = _oidc.Settings.RedirectUri.Authority;
            throw new SignInException(SignInFailure.LoopbackUnavailable, $"Could not listen on the sign-in return address ({authority}). Another app may be using it.", authority, exception);
        }

        using (receiver)
        {
            var authorize = await _oidc.AuthorizeUriAsync(state, nonce, Pkce.Challenge(verifier), cancellationToken).ConfigureAwait(false);
            openBrowser(authorize);
            callback = await receiver.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        if (callback.TryGetValue("error", out var error))
        {
            callback.TryGetValue("error_description", out var description);
            var detail = $"{error}{(string.IsNullOrEmpty(description) ? "" : $": {description}")}";
            throw new SignInException(SignInFailure.Refused, $"The sign-in was refused ({detail}).", detail);
        }

        if (!callback.TryGetValue("state", out var returnedState) || returnedState != state)
        {
            throw new SignInException(SignInFailure.Mismatch, "The sign-in answer does not match the one this PC started (state). Sign in again.", "state");
        }

        if (!callback.TryGetValue("code", out var code) || code.Length == 0)
        {
            throw new SignInException(SignInFailure.NoCode, "The sign-in answer has no code.");
        }

        TokenResponse tokens;
        try
        {
            tokens = await _oidc.ExchangeCodeAsync(code, verifier, cancellationToken).ConfigureAwait(false);
        }
        catch (TokenEndpointException exception)
        {
            throw new SignInException(exception.Failure, exception.Message, exception.Detail, exception);
        }

        var userName = CheckIdToken(tokens.IdToken, nonce);
        if (string.IsNullOrEmpty(tokens.RefreshToken))
        {
            throw new SignInException(SignInFailure.NoRefreshToken, "No refresh token came back (offline_access).");
        }

        Accept(tokens);
        UserName = userName;
        Save(new StoredSession(tokens.RefreshToken, userName));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SignOut()
    {
        _accessToken = null;
        UserName = null;
        if (File.Exists(_file))
        {
            File.Delete(_file);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is { } token && _time.GetUtcNow() < _expiresAt - Margin)
        {
            return token;
        }

        await _refreshing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_accessToken is { } again && _time.GetUtcNow() < _expiresAt - Margin)
            {
                return again;
            }

            var stored = Load();
            if (stored is null)
            {
                return null;
            }

            try
            {
                var tokens = await _oidc.RefreshAsync(stored.RefreshToken, cancellationToken).ConfigureAwait(false);
                Accept(tokens);
                if (!string.IsNullOrEmpty(tokens.RefreshToken) && tokens.RefreshToken != stored.RefreshToken)
                {
                    // The refresh token was rotated. Keep the new one.
                    Save(stored with { RefreshToken = tokens.RefreshToken });
                }

                return _accessToken;
            }
            catch (TokenEndpointException exception) when (exception.NeedsSignIn)
            {
                // Revoked or expired. Ask the user to sign in again.
                SignOut();
                return null;
            }
            catch (TokenEndpointException exception)
            {
                // Just unreachable. Keep the sign-in.
                throw new ServerException(exception.Message, null, null, exception);
            }
        }
        finally
        {
            _refreshing.Release();
        }
    }

    public void Invalidate(string accessToken)
    {
        if (_accessToken == accessToken)
        {
            _accessToken = null;
        }
    }

    public void Dispose() => _refreshing.Dispose();

    private static string? CheckIdToken(string? idToken, string nonce)
    {
        if (idToken is null)
        {
            return null;
        }

        var claims = OidcClient.ReadIdTokenClaims(idToken);
        if (!claims.TryGetValue("nonce", out var returned) || returned.GetString() != nonce)
        {
            throw new SignInException(SignInFailure.Mismatch, "The sign-in answer does not match the one this PC started (nonce). Sign in again.", "nonce");
        }

        foreach (var name in new[] { "preferred_username", "name", "email" })
        {
            if (claims.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text)
            {
                return text;
            }
        }

        return null;
    }

    private void Accept(TokenResponse tokens)
    {
        _accessToken = tokens.AccessToken;
        _expiresAt = _time.GetUtcNow() + TimeSpan.FromSeconds(tokens.ExpiresIn ?? 300);
    }

    private StoredSession? Load()
    {
        if (!File.Exists(_file))
        {
            return null;
        }

        try
        {
            var json = Encoding.UTF8.GetString(_protector.Unprotect(File.ReadAllBytes(_file)));
            return JsonSerializer.Deserialize<StoredSession>(json);
        }
        catch (Exception exception) when (exception is JsonException or IOException or System.Security.Cryptography.CryptographicException)
        {
            // E.g. encrypted by another user or on another PC. If unreadable, ask the user to sign in again.
            return null;
        }
    }

    private void Save(StoredSession session)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(_file));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var bytes = _protector.Protect(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(session)));
        var temporary = _file + ".tmp";
        File.WriteAllBytes(temporary, bytes);
        File.Move(temporary, _file, overwrite: true);
    }

    private sealed record StoredSession(string RefreshToken, string? UserName);
}
