using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace OAuthSamples;

/// <summary>OpenID Connect Core 3.1.3.7 ID token validation (code flow, token endpoint response) and 5.3.4 UserInfo sub check.</summary>
public static class IdTokenValidator
{
    public static async Task<(bool Ok, string? Error)> ValidateAsync(
        string idToken, string issuer, string clientId, string expectedNonce, IEnumerable<SecurityKey> keys,
        DateTimeOffset? now = null)
    {
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidIssuer = issuer,            // must equal the discovered issuer
            ValidateAudience = true, ValidAudience = clientId,      // aud must contain my client_id
            ValidateLifetime = true, RequireExpirationTime = true,
            ClockSkew = TokenRules.ClockSkew,
            RequireSignedTokens = true, ValidateIssuerSigningKey = true,
            ValidAlgorithms = TokenRules.AllowedAlgorithms,
            IssuerSigningKeys = keys,
            LifetimeValidator = now is null ? null : (nbf, exp, _, p) =>
                (exp is null || now.Value.UtcDateTime <= exp.Value.Add(p.ClockSkew)) && (nbf is null || now.Value.UtcDateTime >= nbf.Value.Subtract(p.ClockSkew))
        };

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(idToken, parameters);
        if (!result.IsValid) return (false, result.Exception?.GetType().Name ?? "invalid");

        var jwt = (JsonWebToken)result.SecurityToken;
        if (jwt.Audiences.Count() > 1) return (false, "additional audiences not trusted");          // Core 3.1.3.7 step 3
        if (string.IsNullOrEmpty(jwt.Subject) || jwt.Subject.Length > 255) return (false, "bad sub");  // Core 2: sub required, max 255
        if (!jwt.TryGetPayloadValue<long>("iat", out _)) return (false, "iat missing");               // Core 2: iat required
        if (!jwt.TryGetPayloadValue<string>("nonce", out var nonce) || !string.Equals(nonce, expectedNonce, StringComparison.Ordinal))
            return (false, "nonce mismatch");                                                          // Core 3.1.3.7 step 11
        return (true, null);
    }

    /// <summary>Core 5.3.4: the UserInfo "sub" MUST exactly match the ID token "sub", or the response is not for this user.</summary>
    public static bool UserInfoSubMatches(string idTokenSub, string userInfoJson)
    {
        using var doc = JsonDocument.Parse(userInfoJson);
        return doc.RootElement.TryGetProperty("sub", out var sub)
            && sub.ValueKind == JsonValueKind.String
            && string.Equals(sub.GetString(), idTokenSub, StringComparison.Ordinal);
    }
}
