using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace OAuthSamples;

/// <summary>Executable checks for every sample. Run: dotnet run -- selftest</summary>
public static class SelfTest
{
    private static int _fail;
    private static int _count;

    private static void Check(string name, bool ok)
    {
        _count++;
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name}");
        if (!ok) _fail++;
    }

    public static async Task<int> RunAsync()
    {
        OAuthChecks();
        await TokenChecks();
        ScimChecks();
        Console.WriteLine(_fail == 0 ? $"ALL PASSED ({_count} checks)" : $"{_fail} FAILED of {_count}");
        return _fail == 0 ? 0 : 1;
    }

    // ---------- step 1: OAuth ----------
    private static void OAuthChecks()
    {
        Check("pkce rfc7636 appendix B vector",
            Pkce.ChallengeFor("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk") == "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM");
        var (v, c) = Pkce.Create();
        Check("pkce verifier length 43", v.Length == 43);
        Check("pkce verify ok", Pkce.Verify(v, c));
        Check("pkce verify wrong verifier rejected", !Pkce.Verify(Pkce.Create().Verifier, c));
        Check("pkce short verifier rejected", !Pkce.Verify("short", c));

        Check("wellknown with path", Metadata.WellKnown(new Uri("https://example.com/issuer1"), "oauth-authorization-server").ToString()
            == "https://example.com/.well-known/oauth-authorization-server/issuer1");
        Check("wellknown no path", Metadata.WellKnown(new Uri("https://example.com"), "oauth-authorization-server").ToString()
            == "https://example.com/.well-known/oauth-authorization-server");
        Check("wellknown trailing slash", Metadata.WellKnown(new Uri("https://example.com/issuer1/"), "oauth-authorization-server").ToString()
            == "https://example.com/.well-known/oauth-authorization-server/issuer1");
        Check("wellknown rfc9728 example", Metadata.WellKnown(new Uri("https://resource.example.com/resource1"), "oauth-protected-resource").AbsolutePath
            == "/.well-known/oauth-protected-resource/resource1");
        Check("identifier mismatch rejected", !Metadata.IdentifierMatches("https://a.example", "https://a.example/"));

        // OIDC Discovery 4.1 examples
        Check("oidc config no path", Metadata.OidcConfiguration(new Uri("https://example.com")).ToString()
            == "https://example.com/.well-known/openid-configuration");
        Check("oidc config with path (append)", Metadata.OidcConfiguration(new Uri("https://example.com/issuer1")).ToString()
            == "https://example.com/issuer1/.well-known/openid-configuration");
        Check("oidc config trailing slash removed", Metadata.OidcConfiguration(new Uri("https://example.com/issuer1/")).ToString()
            == "https://example.com/issuer1/.well-known/openid-configuration");

        var reg = new[] { "https://app.example/cb", "http://127.0.0.1/cb" };
        Check("redirect exact ok", Metadata.RedirectUriAllowed("https://app.example/cb", reg));
        Check("redirect prefix rejected", !Metadata.RedirectUriAllowed("https://app.example/cb/evil", reg));
        Check("redirect subdomain rejected", !Metadata.RedirectUriAllowed("https://evil.app.example/cb", reg));
        Check("redirect loopback any port", Metadata.RedirectUriAllowed("http://127.0.0.1:53211/cb", reg));
        Check("redirect non-loopback port rejected", !Metadata.RedirectUriAllowed("https://app.example:444/cb", reg));

        var basic = TokenClient.Basic("my client", "s:ecret");
        Check("basic form-urlencoded", Encoding.UTF8.GetString(Convert.FromBase64String(basic.Parameter!)) == "my+client:s%3Aecret");

        using var dpop = new DpopSigner();
        var proof = dpop.CreateProof("GET", new Uri("https://api.example/orders?x=1"), "tok123");
        var parts = proof.Split('.');
        var hdr = JsonDocument.Parse(B64(parts[0])).RootElement;
        var pl = JsonDocument.Parse(B64(parts[1])).RootElement;
        Check("dpop typ", hdr.GetProperty("typ").GetString() == "dpop+jwt");
        Check("dpop htu has no query", pl.GetProperty("htu").GetString() == "https://api.example/orders");
        Check("dpop ath", pl.GetProperty("ath").GetString() == Pkce.Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes("tok123"))));
        var jwk = hdr.GetProperty("jwk");
        using var pub = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = B64Bytes(jwk.GetProperty("x").GetString()!), Y = B64Bytes(jwk.GetProperty("y").GetString()!) }
        });
        Check("dpop signature verifies", pub.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), B64Bytes(parts[2]), HashAlgorithmName.SHA256));
        Check("dpop jkt is 43 chars", dpop.Thumbprint().Length == 43);
        Check("es256 signature is 64 octets (RFC 7518 3.4 R||S)", B64Bytes(parts[2]).Length == 64);
        Check("library default ClockSkew is 5 minutes", new TokenValidationParameters().ClockSkew == TimeSpan.FromMinutes(5));

        var form = TokenClient.ClientCredentialsForm("orders:read", "https://api.example");
        Check("client credentials carries resource", form["resource"] == "https://api.example" && form["grant_type"] == "client_credentials");

        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim("scope", "orders:readonly orders:write")]));
        Check("scope split: orders:read is NOT orders:readonly", !ResourceServer.HasScope(user, "orders:read"));
        Check("scope split: orders:write found", ResourceServer.HasScope(user, "orders:write"));
    }

    // ---------- step 2: tokens ----------
    private const string Iss = "https://as.example.test";
    private const string Aud = "https://api.example.test/orders";

    private static string MakeToken(SecurityKey key, string alg, string typ, string iss = Iss, string aud = Aud,
        TimeSpan? expiresIn = null, Dictionary<string, object>? extra = null)
    {
        var exp = DateTime.UtcNow + (expiresIn ?? TimeSpan.FromMinutes(5));
        var d = new SecurityTokenDescriptor
        {
            Issuer = iss, Audience = aud, TokenType = typ,
            NotBefore = exp - TimeSpan.FromHours(1), IssuedAt = exp - TimeSpan.FromHours(1), Expires = exp,
            Claims = new Dictionary<string, object> { ["sub"] = "user-1", ["scope"] = "orders:read", ["client_id"] = "c1", ["jti"] = Guid.NewGuid().ToString() },
            SigningCredentials = new SigningCredentials(key, alg)
        };
        if (extra is not null) foreach (var kv in extra) d.Claims[kv.Key] = kv.Value;
        return new JsonWebTokenHandler().CreateToken(d);
    }

    private static async Task<bool> Accepts(string token, params SecurityKey[] keys)
    {
        var p = TokenRules.ForAccessTokens(Iss, Aud);
        p.IssuerSigningKeys = keys;
        return (await new JsonWebTokenHandler().ValidateTokenAsync(token, p)).IsValid;
    }

    private static async Task TokenChecks()
    {
        using var rsa = RSA.Create(2048);
        using var rsa2 = RSA.Create(2048);
        var keyA = new RsaSecurityKey(rsa) { KeyId = "k1" };
        var keyB = new RsaSecurityKey(rsa2) { KeyId = "k2" };
        var pubA = new RsaSecurityKey(rsa.ExportParameters(false)) { KeyId = "k1" };

        // happy path
        var good = MakeToken(keyA, SecurityAlgorithms.RsaSha256, "at+jwt");
        Check("jwt: valid RS256 at+jwt accepted", await Accepts(good, pubA));

        // pitfall: alg none
        string B(string json) => Pkce.Base64Url(Encoding.UTF8.GetBytes(json));
        var exp = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds();
        var none = B("{\"alg\":\"none\",\"typ\":\"at+jwt\"}") + "." +
                   B($"{{\"iss\":\"{Iss}\",\"aud\":\"{Aud}\",\"sub\":\"u\",\"exp\":{exp}}}") + ".";
        Check("jwt: alg=none rejected", !await Accepts(none, pubA));
        // control: the same token is well-formed, so the rejection above is caused by our rules and not by a typo in the test token
        var loose = new TokenValidationParameters
        {
            ValidIssuer = Iss, ValidAudience = Aud, RequireSignedTokens = false, ValidateIssuerSigningKey = false,
            ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidTypes = ["at+jwt"]
        };
        Check("jwt control: alg=none token is accepted by deliberately loose parameters", (await new JsonWebTokenHandler().ValidateTokenAsync(none, loose)).IsValid);

        // pitfall: algorithm confusion (HS256 signed with the RSA public key bytes as the secret)
        var hmacKey = new SymmetricSecurityKey(rsa.ExportSubjectPublicKeyInfo()) { KeyId = "k1" };
        var confused = MakeToken(hmacKey, SecurityAlgorithms.HmacSha256, "at+jwt");
        Check("jwt: HS256 token signed with RSA public key rejected", !await Accepts(confused, pubA));
        Check("jwt: HS256 token rejected even if the verifier key is the shared secret", !await Accepts(confused, hmacKey));

        // pitfall: issuer, audience, type, signature, expiry, skew
        Check("jwt: wrong issuer rejected", !await Accepts(MakeToken(keyA, SecurityAlgorithms.RsaSha256, "at+jwt", iss: "https://evil.example"), pubA));
        Check("jwt: issuer with trailing slash rejected (exact match)", !await Accepts(MakeToken(keyA, SecurityAlgorithms.RsaSha256, "at+jwt", iss: Iss + "/"), pubA));
        Check("jwt: wrong audience rejected", !await Accepts(MakeToken(keyA, SecurityAlgorithms.RsaSha256, "at+jwt", aud: "https://other.example"), pubA));
        Check("jwt: typ JWT (e.g. an ID token) rejected", !await Accepts(MakeToken(keyA, SecurityAlgorithms.RsaSha256, "JWT"), pubA));
        Check("jwt: application/at+jwt typ accepted", await Accepts(MakeToken(keyA, SecurityAlgorithms.RsaSha256, "application/at+jwt"), pubA));
        Check("jwt: token signed by another key rejected", !await Accepts(MakeToken(keyB, SecurityAlgorithms.RsaSha256, "at+jwt"), pubA));
        Check("jwt: expired 10s ago accepted within 30s skew", await Accepts(MakeToken(keyA, SecurityAlgorithms.RsaSha256, "at+jwt", expiresIn: TimeSpan.FromSeconds(-10)), pubA));
        Check("jwt: expired 120s ago rejected", !await Accepts(MakeToken(keyA, SecurityAlgorithms.RsaSha256, "at+jwt", expiresIn: TimeSpan.FromSeconds(-120)), pubA));
        var tampered = good.Split('.');
        tampered[1] = B(Encoding.UTF8.GetString(B64Bytes(tampered[1])).Replace("user-1", "admin"));
        Check("jwt: payload tampering rejected", !await Accepts(string.Join('.', tampered), pubA));

        // key rotation with kid
        var clockNow = DateTimeOffset.UtcNow;
        var published = new List<SecurityKey> { pubA };
        var resolver = new RotatingKeyResolver(() => published.ToList(), TimeSpan.FromMinutes(5), () => clockNow);
        var p = TokenRules.ForAccessTokens(Iss, Aud);
        p.IssuerSigningKeyResolver = resolver.Resolve;
        var handler = new JsonWebTokenHandler();
        Check("rotation: token with known kid valid", (await handler.ValidateTokenAsync(good, p)).IsValid);
        var tokenB = MakeToken(keyB, SecurityAlgorithms.RsaSha256, "at+jwt");
        Check("rotation: unknown kid before publication rejected", !(await handler.ValidateTokenAsync(tokenB, p)).IsValid);
        var fetchesAfterMiss = resolver.Fetches;
        published.Add(new RsaSecurityKey(rsa2.ExportParameters(false)) { KeyId = "k2" });   // AS rotates: new key appears in the JWKS
        Check("rotation: unknown kid does not refetch inside the minimum interval", !(await handler.ValidateTokenAsync(tokenB, p)).IsValid && resolver.Fetches == fetchesAfterMiss);
        clockNow += TimeSpan.FromMinutes(6);
        Check("rotation: after the interval the new key is fetched and token accepted", (await handler.ValidateTokenAsync(tokenB, p)).IsValid && resolver.Fetches == fetchesAfterMiss + 1);
        Check("rotation: old key still validates during overlap", (await handler.ValidateTokenAsync(good, p)).IsValid);

        // JWK Set publication / consumption
        var jwksJson = Jwks.PublicJwksJson([("k1", rsa), ("k2", rsa2)]);
        Check("jwks: published set has no private members", !Jwks.ContainsPrivateMaterial(jwksJson));
        var consumed = Jwks.SigningKeys(jwksJson);
        Check("jwks: consumer reads two signing keys with kids", consumed.Count == 2 && consumed.Select(k => k.KeyId).OrderBy(x => x).SequenceEqual(["k1", "k2"]));
        Check("jwks: token validates with a key parsed from JWKS", await Accepts(good, consumed.First(k => k.KeyId == "k1")));
        Check("jwks: private member detector works", Jwks.ContainsPrivateMaterial("{\"keys\":[{\"kty\":\"RSA\",\"d\":\"x\"}]}"));

        // OIDC ID token
        const string clientId = "client-1";
        var idKey = new RsaSecurityKey(rsa) { KeyId = "k1" };
        string Id(string nonce = "n-123", string aud = clientId, string? extraAud = null)
        {
            var d = new SecurityTokenDescriptor
            {
                Issuer = Iss, TokenType = "JWT", Expires = DateTime.UtcNow.AddMinutes(5), IssuedAt = DateTime.UtcNow, NotBefore = DateTime.UtcNow.AddSeconds(-1),
                Claims = new Dictionary<string, object> { ["sub"] = "user-1", ["nonce"] = nonce, ["aud"] = extraAud is null ? aud : new[] { aud, extraAud } },
                SigningCredentials = new SigningCredentials(idKey, SecurityAlgorithms.RsaSha256)
            };
            return new JsonWebTokenHandler().CreateToken(d);
        }
        Check("oidc: valid ID token accepted", (await IdTokenValidator.ValidateAsync(Id(), Iss, clientId, "n-123", [pubA])).Ok);
        Check("oidc: wrong nonce rejected", !(await IdTokenValidator.ValidateAsync(Id(nonce: "other"), Iss, clientId, "n-123", [pubA])).Ok);
        Check("oidc: aud without my client_id rejected", !(await IdTokenValidator.ValidateAsync(Id(aud: "someone-else"), Iss, clientId, "n-123", [pubA])).Ok);
        Check("oidc: extra untrusted audience rejected", !(await IdTokenValidator.ValidateAsync(Id(extraAud: "evil"), Iss, clientId, "n-123", [pubA])).Ok);
        Check("oidc: wrong issuer rejected", !(await IdTokenValidator.ValidateAsync(Id(), "https://evil.example", clientId, "n-123", [pubA])).Ok);
        Check("oidc: userinfo sub must match", IdTokenValidator.UserInfoSubMatches("user-1", "{\"sub\":\"user-1\",\"name\":\"A\"}")
            && !IdTokenValidator.UserInfoSubMatches("user-1", "{\"sub\":\"user-2\"}") && !IdTokenValidator.UserInfoSubMatches("user-1", "{\"name\":\"A\"}"));
    }

    // ---------- SCIM ----------
    private static void ScimChecks()
    {
        using var err = JsonDocument.Parse(Scim.ErrorBody(409, "uniqueness", "userName taken"));
        var r = err.RootElement;
        Check("scim: error body has schema, string status, scimType", r.GetProperty("schemas")[0].GetString() == Scim.ErrorSchema
            && r.GetProperty("status").ValueKind == JsonValueKind.String && r.GetProperty("status").GetString() == "409"
            && r.GetProperty("scimType").GetString() == "uniqueness");
        Check("scim: media type", Scim.MediaType == "application/scim+json");

        JsonElement J(string s) => JsonDocument.Parse(s).RootElement.Clone();
        const string schema = "\"schemas\":[\"urn:ietf:params:scim:api:messages:2.0:PatchOp\"]";
        Check("scim patch: valid request accepted", Scim.ValidatePatch(J($"{{{schema},\"Operations\":[{{\"op\":\"replace\",\"path\":\"active\",\"value\":false}}]}}")).Ok);
        Check("scim patch: missing PatchOp schema rejected", !Scim.ValidatePatch(J("{\"schemas\":[\"x\"],\"Operations\":[{\"op\":\"add\",\"value\":1}]}")).Ok);
        Check("scim patch: empty Operations rejected", !Scim.ValidatePatch(J($"{{{schema},\"Operations\":[]}}")).Ok);
        Check("scim patch: unknown op (move) rejected", !Scim.ValidatePatch(J($"{{{schema},\"Operations\":[{{\"op\":\"move\",\"path\":\"a\"}}]}}")).Ok);
        var rm = Scim.ValidatePatch(J($"{{{schema},\"Operations\":[{{\"op\":\"remove\"}}]}}"));
        Check("scim patch: remove without path rejected with noTarget", !rm.Ok && rm.ScimType == "noTarget");

        Check("scim paging: startIndex 0 becomes 1, negative count becomes 0", Scim.NormalizePaging(0, -5, 100) == (1, 0));
        Check("scim paging: count capped at server max", Scim.NormalizePaging(3, 5000, 100) == (3, 100));
        Check("scim paging: defaults", Scim.NormalizePaging(null, null, 100) == (1, 100));
    }

    private static byte[] B64Bytes(string s) => Convert.FromBase64String(s.Replace('-', '+').Replace('_', '/').PadRight((s.Length + 3) / 4 * 4, '='));
    private static string B64(string s) => Encoding.UTF8.GetString(B64Bytes(s));
}
