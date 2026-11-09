using System.Xml.Linq;
using NolumiaScheduler.Infrastructure.Auth;

namespace NolumiaScheduler.CoreTests.Server;

/// <summary>
/// The sign-in wording follows the app's language: Infrastructure names the kind of failure and takes
/// the page to show from the caller; the words live in both resource files.
/// </summary>
[TestClass]
public class SignInWordingTests
{
    private static readonly string[] PageKeys = ["SignInPageHeading", "SignInPageBody", "SignInFailureWithDetail"];

    [TestMethod]
    [DataRow("AppResources.resx")]
    [DataRow("AppResources.ja.resx")]
    public void ログインの失敗の種類ごとに文言が両方の言語にある(string file)
    {
        var keys = ResourceKeys(file);
        var expected = Enum.GetNames<SignInFailure>().Select(n => "SignInFailure" + n).Concat(PageKeys);

        var missing = expected.Where(k => !keys.Contains(k)).ToList();

        Assert.AreEqual(0, missing.Count, $"{file} に無い: {string.Join(", ", missing)}");
    }

    [TestMethod]
    public void 戻ったときの画面は渡された言語と文言で作り文字は逃がす()
    {
        var html = new SignInPage("ja", "A<B", "ログインしました", "閉じて & 戻る").ToHtml();

        StringAssert.Contains(html, "<html lang=\"ja\">");
        StringAssert.Contains(html, "<title>A&lt;B</title>");
        StringAssert.Contains(html, "ログインしました");
        StringAssert.Contains(html, "閉じて &amp; 戻る");
    }

    [TestMethod]
    public void 既定の画面は英語()
    {
        StringAssert.Contains(SignInPage.English.ToHtml(), "<html lang=\"en\">");
        StringAssert.Contains(SignInPage.English.ToHtml(), "Signed in to NolumiaScheduler");
    }

    [TestMethod]
    public async Task 戻り先は渡された画面を返す()
    {
        var page = new SignInPage("ja", "NolumiaScheduler", "NolumiaScheduler にログインしました", "このタブは閉じてかまいません。");
        using var receiver = new LoopbackReceiver(new Uri("http://127.0.0.1:0/callback"), page: page);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var waiting = receiver.WaitAsync(timeout.Token);

        using var http = new HttpClient();
        var body = await http.GetStringAsync($"http://127.0.0.1:{receiver.Port}/callback?code=c&state=s", timeout.Token);
        var query = await waiting;

        Assert.AreEqual("c", query["code"]);
        StringAssert.Contains(body, "<html lang=\"ja\">");
        StringAssert.Contains(body, "NolumiaScheduler にログインしました");
    }

    [TestMethod]
    public void 失敗は種類と変わる部分を持ち記録用の文は英語()
    {
        var inner = new TokenEndpointException("invalid_grant", SignInFailure.ServiceRefused, "The sign-in service refused (400 invalid_grant).", "400 invalid_grant");
        var error = new SignInException(inner.Failure, inner.Message, inner.Detail, inner);

        Assert.AreEqual(SignInFailure.ServiceRefused, error.Failure);
        Assert.AreEqual("400 invalid_grant", error.Detail);
        Assert.IsTrue(inner.NeedsSignIn);
        Assert.IsFalse(error.Message.Any(c => c > 0x3000), error.Message);
    }

    private static HashSet<string> ResourceKeys(string file)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NolumiaScheduler.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.IsNotNull(directory, "NolumiaScheduler.slnx が見つからない");
        var path = Path.Combine(directory.FullName, "NolumiaScheduler.Presentation", "Resources", "Strings", file);
        return XDocument.Load(path).Root!.Elements("data")
            .Select(e => (string?)e.Attribute("name"))
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
    }
}
