using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace OAuthSamples;

/// <summary>Protected resource: RFC 6750 bearer validation, RFC 9728 metadata, RFC 8707 audience binding.</summary>
public static class ResourceServer
{
    public static void AddApiAuth(this WebApplicationBuilder b, string issuer, string audience, string metadataUrl)
    {
        b.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
        {
            o.Authority = issuer;                    // discovery + JWKS via AS metadata
            o.RequireHttpsMetadata = true;
            o.MapInboundClaims = false;              // keep "sub", "scope" as issued
            o.RefreshOnIssuerKeyNotFound = true;     // re-read the JWKS when a new kid appears (key rotation)
            o.TokenValidationParameters = TokenRules.ForAccessTokens(issuer, audience);   // issuer, audience, alg, typ, lifetime

            o.Events = new JwtBearerEvents
            {
                OnChallenge = ctx =>
                {
                    ctx.HandleResponse();
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    // RFC 6750 3: no error code when no credentials were sent; invalid_token when a token failed.
                    var error = ctx.AuthenticateFailure is null ? "" : "error=\"invalid_token\", ";
                    ctx.Response.Headers.WWWAuthenticate = $"Bearer {error}resource_metadata=\"{metadataUrl}\"";
                    return Task.CompletedTask;
                },
                OnForbidden = ctx =>
                {
                    ctx.Response.Headers.WWWAuthenticate =
                        $"Bearer error=\"insufficient_scope\", resource_metadata=\"{metadataUrl}\"";
                    return Task.CompletedTask;
                }
            };
        });

        b.Services.AddAuthorizationBuilder()
            .AddPolicy("orders:read", p => p.RequireAuthenticatedUser().RequireAssertion(c => HasScope(c.User, "orders:read")));
    }

    /// <summary>"scope" is a space-delimited string (RFC 9068 2.2.3); never use Contains() on the raw string.</summary>
    public static bool HasScope(ClaimsPrincipal user, string scope) =>
        user.FindAll("scope").SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Contains(scope, StringComparer.Ordinal);

    public static void MapProtectedResourceMetadata(this WebApplication app, string resource, string authorizationServer)
    {
        var path = Metadata.WellKnown(new Uri(resource), "oauth-protected-resource").AbsolutePath;
        app.MapGet(path, () => Results.Json(new Dictionary<string, object>
        {
            ["resource"] = resource,
            ["authorization_servers"] = new[] { authorizationServer },
            ["scopes_supported"] = new[] { "orders:read" },
            ["bearer_methods_supported"] = new[] { "header" }
        })).AllowAnonymous();
    }
}
