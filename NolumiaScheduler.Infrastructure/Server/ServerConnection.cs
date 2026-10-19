using NolumiaScheduler.Infrastructure.Auth;

namespace NolumiaScheduler.Infrastructure.Server;

/// <summary>
/// Everything needed to talk to the Scheduler server, built once at the composition root:
/// the settings (<c>server.json</c>), the sign-in (refresh token in <c>auth.bin</c>, encrypted by
/// <paramref name="protector"/>) and the API client. Keeps the WinUI shell to one line of wiring.
/// </summary>
public sealed class ServerConnection : IDisposable
{
    public const string AuthFileName = "auth.bin";
    public const string CacheFileName = "server-cache.json";

    private readonly HttpClient _http;

    public ServerConnection(string dataDirectory, ISecretProtector protector, HttpMessageHandler? handler = null)
    {
        DataDirectory = dataDirectory;
        Settings = ServerSettings.Load(dataDirectory);
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(30);
        SignIn = new SignInSession(new OidcClient(_http, Settings.Oidc), protector, Path.Combine(dataDirectory, AuthFileName));
        Api = new SchedulerApiClient(_http, Settings.BaseUri, SignIn);
    }

    public string DataDirectory { get; }
    public ServerSettings Settings { get; }
    public SignInSession SignIn { get; }
    public SchedulerApiClient Api { get; }

    public string CacheFile => Path.Combine(DataDirectory, CacheFileName);

    public void Dispose()
    {
        SignIn.Dispose();
        _http.Dispose();
    }
}
