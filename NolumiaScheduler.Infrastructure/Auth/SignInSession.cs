using System.Text;
using System.Text.Json;
using NolumiaScheduler.Infrastructure.Server;

namespace NolumiaScheduler.Infrastructure.Auth;

/// <summary>
/// Scheduler のサーバーへ付けるトークンを持つ（サーバーの ADR-0054 決定 7。WinTrack の同名のものを写した）。
/// 更新トークンは <see cref="ISecretProtector"/> で暗号化してファイルに置き、アクセストークン（300 秒）はメモリにだけ持つ。
/// </summary>
public sealed class SignInSession : IAccessTokenSource, IDisposable
{
    /// <summary>切れる少し前に取り直す。</summary>
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

    /// <summary>ログインしている人の名前（ID トークンの preferred_username など）。ログインしていなければ null。</summary>
    public string? UserName { get; private set; }

    public bool IsSignedIn => File.Exists(_file);

    /// <summary>ログインの状態が変わった（入った・出た・切れた）。</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// ログインする。ブラウザでログイン画面を開き、戻り先（127.0.0.1）で結果を受ける。
    /// </summary>
    /// <param name="openBrowser">ログイン画面を既定のブラウザで開く。</param>
    public async Task SignInAsync(Action<Uri> openBrowser, CancellationToken cancellationToken)
    {
        var state = Pkce.RandomToken();
        var nonce = Pkce.RandomToken();
        var verifier = Pkce.RandomToken();

        IReadOnlyDictionary<string, string> callback;
        LoopbackReceiver receiver;
        try
        {
            receiver = new LoopbackReceiver(_oidc.Settings.RedirectUri);
        }
        catch (System.Net.Sockets.SocketException exception)
        {
            throw new SignInException($"ログインの戻り先（{_oidc.Settings.RedirectUri.Authority}）を開けません。ほかのアプリが使っていないか確かめてください。", exception);
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
            throw new SignInException($"ログインできませんでした（{error}{(string.IsNullOrEmpty(description) ? "" : $": {description}")}）。");
        }

        if (!callback.TryGetValue("state", out var returnedState) || returnedState != state)
        {
            throw new SignInException("ログインの応答が、この PC から始めたものと合いません（state）。もう一度ログインしてください。");
        }

        if (!callback.TryGetValue("code", out var code) || code.Length == 0)
        {
            throw new SignInException("ログインの応答にコードがありません。");
        }

        TokenResponse tokens;
        try
        {
            tokens = await _oidc.ExchangeCodeAsync(code, verifier, cancellationToken).ConfigureAwait(false);
        }
        catch (TokenEndpointException exception)
        {
            throw new SignInException(exception.Message, exception);
        }

        var userName = CheckIdToken(tokens.IdToken, nonce);
        if (string.IsNullOrEmpty(tokens.RefreshToken))
        {
            throw new SignInException("更新トークンが返りませんでした（offline_access）。");
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
                    // 更新トークンが回された。新しいほうを残す。
                    Save(stored with { RefreshToken = tokens.RefreshToken });
                }

                return _accessToken;
            }
            catch (TokenEndpointException exception) when (exception.NeedsSignIn)
            {
                // 取り消された・切れた。もう一度ログインしてもらう。
                SignOut();
                return null;
            }
            catch (TokenEndpointException exception)
            {
                // つながらないだけ。ログインは残す。
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
            throw new SignInException("ログインの応答が、この PC から始めたものと合いません（nonce）。もう一度ログインしてください。");
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
            // 別の利用者・別の PC で暗号化されたものなど。読めなければログインし直してもらう。
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
