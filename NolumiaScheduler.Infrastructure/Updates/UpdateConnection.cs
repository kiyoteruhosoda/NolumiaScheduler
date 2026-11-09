using NolumiaScheduler.Infrastructure.Auth;

namespace NolumiaScheduler.Infrastructure.Updates;

/// <summary>
/// Where new versions of this app come from and the sign-in that unlocks them (task #937, ADR-0009),
/// the same shape as RustEdit: blobshare's <c>/api/app-releases/nolumiascheduler/</c> hands out
/// <c>artifacts/nolumiascheduler/</c> to an assay user token of the public client
/// <c>nolumiascheduler-update</c>, which is bound to blobshare-prod (whoever may use blobshare may
/// update). It is a sign-in of its own, separate from the Scheduler server's, kept in
/// <see cref="AuthFileName"/> (DPAPI) next to the server's <c>auth.bin</c>.
/// </summary>
public sealed class UpdateConnection : IDisposable
{
    public const string AuthFileName = "update-auth.bin";

    /// <summary>The blobshare relay Velopack reads <c>releases.win.json</c> and the packages from.</summary>
    public static readonly Uri ReleasesUri = new("https://share.nolumia.com/api/app-releases/nolumiascheduler/");

    /// <summary>
    /// The client <c>nolumiascheduler-update</c> registered in assay on 2026-11-09. Its loopback port
    /// differs from WinTrack (48653), RustEdit (48654) and the Scheduler server sign-in (48663), so all
    /// can sign in at once; assay compares redirect URIs exactly, so the port is fixed.
    /// </summary>
    public static readonly OidcSettings Oidc = new(
        OidcSettings.DefaultIssuer,
        "588618dc4c16aa9ccc91d623b1a8572a",
        new Uri("http://127.0.0.1:48664/callback"),
        OidcSettings.DefaultScope);

    private readonly HttpClient _http;

    public UpdateConnection(string dataDirectory, ISecretProtector protector, HttpMessageHandler? handler = null)
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(30);
        SignIn = new SignInSession(new OidcClient(_http, Oidc), protector, Path.Combine(dataDirectory, AuthFileName));
    }

    public SignInSession SignIn { get; }

    public void Dispose()
    {
        SignIn.Dispose();
        _http.Dispose();
    }
}
