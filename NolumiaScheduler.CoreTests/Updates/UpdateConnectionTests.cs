using NolumiaScheduler.Infrastructure.Auth;
using NolumiaScheduler.Infrastructure.Server;
using NolumiaScheduler.Infrastructure.Updates;

namespace NolumiaScheduler.CoreTests.Updates;

/// <summary>Task #937 (ADR-0009): updates come from blobshare with a sign-in of their own, as in RustEdit.</summary>
[TestClass]
public class UpdateConnectionTests
{
    private string _dir = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"nolumia-update-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_dir, recursive: true);

    [TestMethod]
    public void Updates_AreFetchedViaBlobshareRelay()
    {
        Assert.AreEqual(
            "https://share.nolumia.com/api/app-releases/nolumiascheduler/",
            UpdateConnection.ReleasesUri.AbsoluteUri);
    }

    [TestMethod]
    public void UpdateSignIn_UsesSeparateClientBoundToBlobshare()
    {
        var server = ServerSettings.Load(_dir).Oidc;

        Assert.AreEqual("588618dc4c16aa9ccc91d623b1a8572a", UpdateConnection.Oidc.ClientId);
        Assert.AreEqual(OidcSettings.DefaultIssuer, UpdateConnection.Oidc.Issuer);
        Assert.AreEqual("http://127.0.0.1:48664/callback", UpdateConnection.Oidc.RedirectUri.AbsoluteUri);
        Assert.AreNotEqual(server.ClientId, UpdateConnection.Oidc.ClientId);
        // Can sign in alongside WinTrack 48653, RustEdit 48654 and the Scheduler server 48663
        Assert.AreNotEqual(server.RedirectUri.Port, UpdateConnection.Oidc.RedirectUri.Port);
        Assert.IsTrue(UpdateConnection.Oidc.Scope.Contains("offline_access", StringComparison.Ordinal));
    }

    [TestMethod]
    public void UpdateSignIn_IsStoredInSeparateFileFromServerSignIn()
    {
        Assert.AreNotEqual(ServerConnection.AuthFileName, UpdateConnection.AuthFileName);

        using var updates = new UpdateConnection(_dir, new PlainProtector());
        using var server = new ServerConnection(_dir, new PlainProtector());

        Assert.IsFalse(updates.SignIn.IsSignedIn);
        File.WriteAllBytes(Path.Combine(_dir, ServerConnection.AuthFileName), [1]);
        Assert.IsTrue(server.SignIn.IsSignedIn);
        Assert.IsFalse(updates.SignIn.IsSignedIn);
    }

    private sealed class PlainProtector : ISecretProtector
    {
        public byte[] Protect(byte[] plain) => plain;

        public byte[] Unprotect(byte[] protectedData) => protectedData;
    }
}
