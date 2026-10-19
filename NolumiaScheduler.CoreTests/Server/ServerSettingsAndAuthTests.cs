using NolumiaScheduler.Infrastructure.Auth;
using NolumiaScheduler.Infrastructure.Server;

namespace NolumiaScheduler.CoreTests.Server;

[TestClass]
public class ServerSettingsAndAuthTests
{
    private string _dir = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"nolumia-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_dir, recursive: true);

    [TestMethod]
    public void 設定のファイルが無ければ本番とWindowsアプリのclientを向く()
    {
        var settings = ServerSettings.Load(_dir);

        Assert.AreEqual("https://scheduler.nolumia.com/", settings.BaseUri.AbsoluteUri);
        Assert.AreEqual("f0c601eaa4ea98d17f8ebdc73eb70761", settings.Oidc.ClientId);
        Assert.AreEqual("http://127.0.0.1:48663/callback", settings.Oidc.RedirectUri.AbsoluteUri);
        Assert.IsTrue(settings.Oidc.IsConfigured);
    }

    [TestMethod]
    public void 設定のファイルで向き先を差し替えられる()
    {
        File.WriteAllText(Path.Combine(_dir, ServerSettings.FileName),
            """{ "baseUrl": "https://schedulerstg.nolumia.com", "clientId": "other" }""");

        var settings = ServerSettings.Load(_dir);

        Assert.AreEqual("https://schedulerstg.nolumia.com/", settings.BaseUri.AbsoluteUri);
        Assert.AreEqual("other", settings.Oidc.ClientId);
        Assert.AreEqual(OidcSettings.DefaultIssuer, settings.Oidc.Issuer);
    }

    [TestMethod]
    public void 読めない設定は既定に戻る()
    {
        File.WriteAllText(Path.Combine(_dir, ServerSettings.FileName), "{ not json");

        Assert.AreEqual(ServerSettings.Defaults, ServerSettings.Load(_dir));
    }

    [TestMethod]
    public void PKCEのchallengeはverifierのSHA256のbase64url()
    {
        // Base64url(SHA-256(verifier)), computed independently.
        Assert.AreEqual("T08pcQoLW-K9jKPm68HZPmIWT5MWIunArWDWzu3ls04",
            Pkce.Challenge("dBjftJeZ4CVP-mJ92K5TxyxWAPpAKMGAqfGnpL2Q0WY"));
    }

    [TestMethod]
    public void 戻り先の要求から値を読む_127001以外は受けない()
    {
        Assert.AreEqual<(string, string)?>(("/callback", "code=a%20b&state=x"), LoopbackReceiver.ParseTarget("GET /callback?code=a%20b&state=x HTTP/1.1"));
        Assert.IsNull(LoopbackReceiver.ParseTarget("POST /callback HTTP/1.1"));
        var query = LoopbackReceiver.ParseQuery("code=a%20b&state=x+y");
        Assert.AreEqual("a b", query["code"]);
        Assert.AreEqual("x y", query["state"]);
        Assert.ThrowsExactly<ArgumentException>(() => new LoopbackReceiver(new Uri("http://example.com:48663/callback")));
    }
}
