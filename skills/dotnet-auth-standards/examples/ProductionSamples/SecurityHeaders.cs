namespace ProductionSamples;

/// <summary>
/// Response headers for an API. Sources: OWASP Secure Headers Project, RFC 6797 (HSTS), W3C CSP 3, Fetch (nosniff).
/// An API returns JSON, so the CSP is "nothing may load": a stray HTML error page cannot run script or be framed.
/// </summary>
public static class SecurityHeaders
{
    public static IServiceCollection AddSampleSecurityHeaders(this IServiceCollection services)
    {
        services.AddHsts(o =>
        {
            o.MaxAge = TimeSpan.FromDays(365);
            o.IncludeSubDomains = true;
            o.ExcludedHosts.Clear();                      // default excludes localhost; cleared here so tests can prove it
            // o.Preload = true;                          // only after the whole domain is HTTPS for good
        });
        return services;
    }

    /// <summary>Call early so headers are present on error responses too (they are added in OnStarting).</summary>
    public static IApplicationBuilder UseSampleSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (ctx, next) =>
        {
            ctx.Response.OnStarting(() =>
            {
                var h = ctx.Response.Headers;
                h.XContentTypeOptions = "nosniff";                                   // no MIME sniffing
                h.XFrameOptions = "DENY";                                            // legacy clickjacking guard
                h.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";
                h["Referrer-Policy"] = "no-referrer";
                h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
                h["Cross-Origin-Resource-Policy"] = "same-origin";
                h["Cross-Origin-Opener-Policy"] = "same-origin";
                if (!h.ContainsKey("Cache-Control")) h.CacheControl = "no-store";    // tokens and personal data must not be cached
                h.Remove("Server");                                                  // also set Kestrel AddServerHeader = false
                h.Remove("X-Powered-By");
                return Task.CompletedTask;
            });
            await next();
        });
}
