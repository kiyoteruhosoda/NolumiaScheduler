namespace NolumiaScheduler.Infrastructure.Auth;

/// <summary>
/// The assay public client the app signs in with (server ADR-0054 decision 7: <c>nolumiascheduler-win</c>,
/// authorization code + PKCE, loopback redirect). The client id is handed over when the client is
/// registered (2026-10-19); <c>server.json</c> (<see cref="Server.ServerSettings"/>) can still override every value.
/// </summary>
public sealed record OidcSettings(Uri Issuer, string ClientId, Uri RedirectUri, string Scope)
{
    public const string DefaultScope = "openid profile email offline_access";

    /// <summary>The Nolumia assay tenant (the same one WinTrack signs in to).</summary>
    public static readonly Uri DefaultIssuer = new("https://identity.nolumia.com/01a00dfe-bffb-7f23-88b5-8bbef50d23f0");

    /// <summary>The client <c>nolumiascheduler-win</c> registered in assay (redirect must match exactly).</summary>
    public const string DefaultClientId = "f0c601eaa4ea98d17f8ebdc73eb70761";

    /// <summary>Registered redirect. WinTrack listens on 48653, so both apps can sign in at once.</summary>
    public static readonly Uri DefaultRedirectUri = new("http://127.0.0.1:48663/callback");

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId);

    public Uri DiscoveryUri => new(Issuer.AbsoluteUri.TrimEnd('/') + "/.well-known/openid-configuration");
}
