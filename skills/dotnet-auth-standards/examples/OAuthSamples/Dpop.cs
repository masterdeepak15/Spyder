using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OAuthSamples;

/// <summary>RFC 9449 DPoP proof creation (client side), ES256, no external library.</summary>
public sealed class DpopSigner : IDisposable
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public Dictionary<string, string> PublicJwk()
    {
        var q = _key.ExportParameters(false).Q;
        return new() { ["kty"] = "EC", ["crv"] = "P-256", ["x"] = Pkce.Base64Url(q.X!), ["y"] = Pkce.Base64Url(q.Y!) };
    }

    /// <summary>RFC 7638 thumbprint: required members in lexicographic order, no whitespace. This is cnf.jkt.</summary>
    public string Thumbprint()
    {
        var j = PublicJwk();
        var canonical = $"{{\"crv\":\"{j["crv"]}\",\"kty\":\"{j["kty"]}\",\"x\":\"{j["x"]}\",\"y\":\"{j["y"]}\"}}";
        return Pkce.Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public string CreateProof(string method, Uri url, string? accessToken = null, string? nonce = null)
    {
        var header = new Dictionary<string, object> { ["typ"] = "dpop+jwt", ["alg"] = "ES256", ["jwk"] = PublicJwk() };
        var claims = new Dictionary<string, object>
        {
            ["jti"] = Guid.NewGuid().ToString(),                     // fresh per request, including retries
            ["htm"] = method,
            ["htu"] = url.GetLeftPart(UriPartial.Path),              // no query, no fragment
            ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        if (accessToken is not null)
            claims["ath"] = Pkce.Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(accessToken)));
        if (nonce is not null) claims["nonce"] = nonce;               // from DPoP-Nonce header

        var signingInput = Enc(header) + "." + Enc(claims);
        // ECDsa.SignData default output is IEEE P1363 (r||s), which is what JWS ES256 requires.
        var sig = _key.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256);
        return signingInput + "." + Pkce.Base64Url(sig);
    }

    private static string Enc(object o) => Pkce.Base64Url(JsonSerializer.SerializeToUtf8Bytes(o));

    public void Dispose() => _key.Dispose();
}
