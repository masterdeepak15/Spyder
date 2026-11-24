using System.Security.Cryptography;
using System.Text;

namespace OAuthSamples;

/// <summary>RFC 7636 PKCE (S256 only). Also required by RFC 9700 for public clients.</summary>
public static class Pkce
{
    // 32 random octets -> 43 base64url chars (RFC 7636 4.1), 256 bits of entropy (7.1).
    public static (string Verifier, string Challenge) Create()
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        return (verifier, ChallengeFor(verifier));
    }

    public static string ChallengeFor(string verifier) =>
        Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    /// <summary>Authorization-server side check at the token endpoint. A false result maps to error=invalid_grant.</summary>
    public static bool Verify(string verifier, string storedChallenge)
    {
        if (verifier.Length is < 43 or > 128) return false;
        foreach (var c in verifier)
        {
            var ok = char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or '~';
            if (!ok) return false;
        }
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(ChallengeFor(verifier)),
            Encoding.ASCII.GetBytes(storedChallenge));
    }

    public static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
