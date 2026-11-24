using Microsoft.IdentityModel.Tokens;

namespace OAuthSamples;

/// <summary>
/// One place for JWT access token validation rules (RFC 7519 7.2, RFC 7515 5.2, RFC 8725 3, RFC 9068 4).
/// Signing keys are NOT set here; they come from discovery (Authority) or from a key resolver.
/// </summary>
public static class TokenRules
{
    // Application decision (RFC 7515 5.2, RFC 8725 3.2): only the algorithms you really use. Never "none", never HS* with public keys.
    public static readonly string[] AllowedAlgorithms = [SecurityAlgorithms.RsaSha256, SecurityAlgorithms.EcdsaSha256];

    public static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);   // RFC 7519 4.1.4: "no more than a few minutes"

    public static TokenValidationParameters ForAccessTokens(string issuer, string audience) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = issuer,                       // exact string match with the AS "issuer" (RFC 9068 4)
        ValidateAudience = true,
        ValidAudience = audience,                   // this API's resource identifier (RFC 8707 / 9068 4)
        ValidateLifetime = true,
        RequireExpirationTime = true,
        ClockSkew = ClockSkew,
        RequireSignedTokens = true,                 // rejects alg=none
        ValidateIssuerSigningKey = true,
        ValidAlgorithms = AllowedAlgorithms,        // blocks HS256-with-RSA-public-key confusion
        ValidTypes = ["at+jwt", "application/at+jwt"]   // RFC 9068 2.1/4: an ID token (typ JWT) is not an access token
    };
}
