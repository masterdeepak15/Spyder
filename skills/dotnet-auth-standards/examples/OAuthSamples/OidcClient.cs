using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace OAuthSamples;

/// <summary>Confidential web client: authorization code + PKCE, no implicit/ROPC (RFC 6749, 7636, 9700).</summary>
public static class OidcClient
{
    public static void AddWebSignIn(this WebApplicationBuilder b)
    {
        b.Services.AddAuthentication(o =>
        {
            o.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            o.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
        })
        .AddCookie(o =>
        {
            o.Cookie.HttpOnly = true;
            o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            o.Cookie.SameSite = SameSiteMode.Lax;   // Lax is needed for the redirect back from the AS
        })
        .AddOpenIdConnect(o =>
        {
            o.Authority = b.Configuration["Oidc:Authority"];          // issuer is validated against discovery
            o.ClientId = b.Configuration["Oidc:ClientId"];
            o.ClientSecret = b.Configuration["Oidc:ClientSecret"];    // from user-secrets / vault, never source
            o.ResponseType = OpenIdConnectResponseType.Code;          // authorization code only
            o.UsePkce = true;                                         // S256 challenge sent and verified by the AS
            o.SaveTokens = true;
            o.MapInboundClaims = false;
            o.Scope.Clear();
            o.Scope.Add("openid");
            o.Scope.Add("profile");
            o.RequireHttpsMetadata = true;
        });
    }
}
