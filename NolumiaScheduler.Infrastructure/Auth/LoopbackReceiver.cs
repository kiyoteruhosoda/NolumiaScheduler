using System.Net;
using System.Net.Sockets;
using System.Text;

namespace NolumiaScheduler.Infrastructure.Auth;

/// <summary>
/// ログインの戻り先（<c>http://127.0.0.1:48653/callback</c>）をその場で受ける（RFC 8252）。
/// HttpListener は 127.0.0.1 の受け口に管理者の予約が要るので、TCP で 1 本だけ受ける。
/// </summary>
public sealed class LoopbackReceiver : IDisposable
{
    private const string DonePage = """
        <!doctype html><html lang="ja"><head><meta charset="utf-8"><title>NolumiaScheduler</title></head>
        <body style="font-family: 'Yu Gothic UI', sans-serif; margin: 3em;">
        <h1 style="font-size: 1.4em;">NolumiaScheduler にログインしました</h1>
        <p>このタブは閉じてかまいません。</p></body></html>
        """;

    private readonly TcpListener _listener;
    private readonly string _path;
    private readonly TimeSpan _readTimeout;

    /// <param name="redirectUri">戻り先。</param>
    /// <param name="readTimeout">
    /// 1 本の接続で要求が届くまで待つ長さ（既定 5 秒）。ブラウザは先回りで中身の無い接続を開くことがあり、
    /// それを待ち続けると本物の戻りを受けられない。
    /// </param>
    public LoopbackReceiver(Uri redirectUri, TimeSpan? readTimeout = null)
    {
        _readTimeout = readTimeout ?? TimeSpan.FromSeconds(5);
        if (!IPAddress.TryParse(redirectUri.Host, out var address) || !IPAddress.IsLoopback(address))
        {
            throw new ArgumentException("戻り先は 127.0.0.1 でなければなりません。", nameof(redirectUri));
        }

        _path = redirectUri.AbsolutePath;
        _listener = new TcpListener(address, redirectUri.Port);
        _listener.Start();
    }

    /// <summary>受けている番号（試験で 0 を渡したときに実際の番号を知るため）。</summary>
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>戻り先への GET を 1 本受け、問い合わせの値を返す。ほかの道（favicon など）には 404 を返して待ち続ける。</summary>
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
                    // 何も送ってこない接続（先回りの接続）。捨てて次を待つ。
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

            await WriteAsync(stream, "200 OK", DonePage, cancellationToken).ConfigureAwait(false);
            return ParseQuery(target.Value.Query);
        }
    }

    public void Dispose() => _listener.Stop();

    internal static (string Path, string Query)? ParseTarget(string? requestLine)
    {
        // 例: "GET /callback?code=...&state=... HTTP/1.1"
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
        // 要求の頭だけ読む（本文は無い）。8 KiB を超える要求は相手にしない。
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
