using System.Security.Cryptography;
using System.Text;

namespace NolumiaScheduler.Infrastructure.Auth;

/// <summary>PKCE（S256）と、state・nonce に使う乱数。</summary>
public static class Pkce
{
    /// <summary>32 バイトの乱数を base64url にしたもの（43 文字）。</summary>
    public static string RandomToken() => Base64Url(RandomNumberGenerator.GetBytes(32));

    public static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    internal static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    internal static byte[] FromBase64Url(string text)
    {
        var padded = text.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            _ => "",
        };
        return Convert.FromBase64String(padded);
    }
}
