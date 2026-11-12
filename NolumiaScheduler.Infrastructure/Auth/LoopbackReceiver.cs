using System.Net;
using System.Net.Sockets;
using System.Text;

namespace NolumiaScheduler.Infrastructure.Auth;

/// <summary>
/// Receives the sign-in redirect (<c>http://127.0.0.1:48653/callback</c>) in place (RFC 8252).
/// HttpListener needs an admin URL reservation for 127.0.0.1, so a single connection is accepted over TCP instead.
/// </summary>
public sealed class LoopbackReceiver : IDisposable
{
    private readonly TcpListener _listener;
    private readonly string _path;
    private readonly TimeSpan _readTimeout;
    private readonly string _donePage;

    /// <param name="redirectUri">The redirect URI.</param>
    /// <param name="readTimeout">
    /// How long to wait for a request on one connection (default 5 seconds). Browsers may open empty preconnect connections,
    /// and waiting on one of them forever would miss the real redirect.
    /// </param>
    /// <param name="page">Page shown in the browser when sign-in returns (in the app language; English by default).</param>
    public LoopbackReceiver(Uri redirectUri, TimeSpan? readTimeout = null, SignInPage? page = null)
    {
        _readTimeout = readTimeout ?? TimeSpan.FromSeconds(5);
        _donePage = (page ?? SignInPage.English).ToHtml();
        if (!IPAddress.TryParse(redirectUri.Host, out var address) || !IPAddress.IsLoopback(address))
        {
            throw new ArgumentException("The redirect URI must be a loopback address (127.0.0.1).", nameof(redirectUri));
        }

        _path = redirectUri.AbsolutePath;
        _listener = new TcpListener(address, redirectUri.Port);
        _listener.Start();
    }

    /// <summary>The port being listened on (so tests that pass 0 can learn the actual port).</summary>
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>Accepts one GET to the redirect path and returns its query values. Other paths (favicon etc.) get 404 and waiting continues.</summary>
    public async Task<IReadOnlyDictionary<string, string>> WaitAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            using var client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            using var stream = client.GetStream();
            string? requestLine;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(_readTimeout);
                try
                {
                    requestLine = await ReadRequestLineAsync(stream, timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // A connection that sends nothing (a preconnect). Drop it and wait for the next.
                    continue;
                }
                catch (IOException)
                {
                    continue;
                }
            }

            if (requestLine is null)
            {
                continue;
            }

            var target = ParseTarget(requestLine);
            if (target is null || !string.Equals(target.Value.Path, _path, StringComparison.Ordinal))
            {
                await WriteAsync(stream, "404 Not Found", "", cancellationToken).ConfigureAwait(false);
                continue;
            }

            await WriteAsync(stream, "200 OK", _donePage, cancellationToken).ConfigureAwait(false);
            return ParseQuery(target.Value.Query);
        }
    }

    public void Dispose() => _listener.Stop();

    internal static (string Path, string Query)? ParseTarget(string? requestLine)
    {
        // e.g. "GET /callback?code=...&state=... HTTP/1.1"
        var parts = requestLine?.Split(' ');
        if (parts is not { Length: 3 } || parts[0] != "GET")
        {
            return null;
        }

        var question = parts[1].IndexOf('?', StringComparison.Ordinal);
        return question < 0 ? (parts[1], "") : (parts[1][..question], parts[1][(question + 1)..]);
    }

    internal static IReadOnlyDictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.IndexOf('=', StringComparison.Ordinal);
            var key = Uri.UnescapeDataString((equals < 0 ? pair : pair[..equals]).Replace('+', ' '));
            var value = equals < 0 ? "" : Uri.UnescapeDataString(pair[(equals + 1)..].Replace('+', ' '));
            values[key] = value;
        }

        return values;
    }

    private static async Task<string?> ReadRequestLineAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        // Read only the request head (there is no body). Requests over 8 KiB are ignored.
        var buffer = new byte[8192];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            length += read;
            var text = Encoding.ASCII.GetString(buffer, 0, length);
            var end = text.IndexOf("\r\n", StringComparison.Ordinal);
            if (end >= 0)
            {
                return text[..end];
            }
        }

        return null;
    }

    private static async Task WriteAsync(NetworkStream stream, string status, string body, CancellationToken cancellationToken)
    {
        var content = Encoding.UTF8.GetBytes(body);
        var head = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {content.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(head, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
    }
}
