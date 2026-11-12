using System.Security.Cryptography;
using System.Text;

namespace NolumiaScheduler.Infrastructure.Auth;

/// <summary>PKCE (S256) and the random values used for state and nonce.</summary>
public static class Pkce
{
    /// <summary>32 random bytes encoded as base64url (43 characters).</summary>
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
