using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace OAuthSamples;

/// <summary>
/// kid-based key lookup with a rate-limited refresh when an unknown kid shows up (key rotation).
/// With Authority + JwtBearer the handler already refreshes on an unknown key (RefreshOnIssuerKeyNotFound);
/// use this only when you validate tokens yourself.
/// </summary>
public sealed class RotatingKeyResolver(Func<IReadOnlyList<SecurityKey>> fetch, TimeSpan minRefreshInterval, Func<DateTimeOffset>? clock = null)
{
    private readonly object _gate = new();
    private readonly Func<DateTimeOffset> _now = clock ?? (() => DateTimeOffset.UtcNow);
    private IReadOnlyList<SecurityKey>? _keys;
    private DateTimeOffset _lastFetch = DateTimeOffset.MinValue;

    public int Fetches { get; private set; }

    public IEnumerable<SecurityKey> Resolve(string token, SecurityToken securityToken, string kid, TokenValidationParameters parameters)
    {
        if (string.IsNullOrEmpty(kid)) return [];            // no kid: do not try every key
        lock (_gate)
        {
            if (_keys is null) Refresh();
            var hit = Match(kid);
            if (hit.Length == 0 && _now() - _lastFetch >= minRefreshInterval)   // unknown kid: refresh at most once per interval
            {
                Refresh();
                hit = Match(kid);
            }
            return hit;
        }
    }

    private SecurityKey[] Match(string kid) => _keys!.Where(k => string.Equals(k.KeyId, kid, StringComparison.Ordinal)).ToArray();

    private void Refresh()
    {
        _keys = fetch();
        _lastFetch = _now();
        Fetches++;
    }
}

public static class Jwks
{
    /// <summary>Publish PUBLIC parameters only (RFC 7517 4, 7); the private exponent "d" must never appear.</summary>
    public static string PublicJwksJson(IEnumerable<(string Kid, RSA Key)> keys)
    {
        var items = keys.Select(k =>
        {
            var p = k.Key.ExportParameters(false);
            return $"{{\"kty\":\"RSA\",\"use\":\"sig\",\"alg\":\"RS256\",\"kid\":\"{k.Kid}\"," +
                   $"\"n\":\"{Pkce.Base64Url(p.Modulus!)}\",\"e\":\"{Pkce.Base64Url(p.Exponent!)}\"}}";
        });
        return "{\"keys\":[" + string.Join(",", items) + "]}";
    }

    /// <summary>Consume a JWK Set: keep signature keys only, ignore unsupported entries (RFC 7517 5).</summary>
    public static IReadOnlyList<SecurityKey> SigningKeys(string jwksJson)
    {
        var set = new JsonWebKeySet(jwksJson);
        return set.GetSigningKeys().ToList();
    }

    public static bool ContainsPrivateMaterial(string jwksJson)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(jwksJson);
        foreach (var key in doc.RootElement.GetProperty("keys").EnumerateArray())
            foreach (var name in new[] { "d", "p", "q", "dp", "dq", "qi", "k" })
                if (key.TryGetProperty(name, out _)) return true;
        return false;
    }

    public static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);
}
