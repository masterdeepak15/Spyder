using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace ProductionSamples;

/// <summary>
/// Built-in ASP.NET Core rate limiting (Microsoft.AspNetCore.RateLimiting): the four algorithms,
/// per-client partitioning and a 429 response that carries Retry-After (RFC 9110 section 10.2.3).
/// </summary>
public static class RateLimiting
{
    public const string Fixed = "fixed";
    public const string Sliding = "sliding";
    public const string Bucket = "bucket";
    public const string Concurrency = "concurrency";
    public const string PerClient = "per-client";

    /// <summary>Limits are parameters so tests can use small numbers; production defaults are in <see cref="Defaults"/>.</summary>
    public sealed record Settings(int Permits, TimeSpan Window, int ConcurrentRequests)
    {
        public static Settings Defaults { get; } = new(100, TimeSpan.FromMinutes(1), 10);
    }

    public static IServiceCollection AddSampleRateLimiting(this IServiceCollection services, Settings s) =>
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = WriteRejection;

            // 1. Fixed window: simple, allows a burst of 2x at a window boundary.
            o.AddFixedWindowLimiter(Fixed, l =>
            {
                l.PermitLimit = s.Permits;
                l.Window = s.Window;
                l.QueueLimit = 0;                      // reject at once; queued requests hold sockets and threads
            });

            // 2. Sliding window: window split into segments, smooths the boundary burst.
            o.AddSlidingWindowLimiter(Sliding, l =>
            {
                l.PermitLimit = s.Permits;
                l.Window = s.Window;
                l.SegmentsPerWindow = 4;
                l.QueueLimit = 0;
            });

            // 3. Token bucket: allows bursts up to TokenLimit, refills steadily.
            o.AddTokenBucketLimiter(Bucket, l =>
            {
                l.TokenLimit = s.Permits;
                l.TokensPerPeriod = Math.Max(1, s.Permits / 2);
                l.ReplenishmentPeriod = s.Window;
                l.QueueLimit = 0;
            });

            // 4. Concurrency: caps in-flight requests (protects slow downstream calls), not request rate.
            o.AddConcurrencyLimiter(Concurrency, l =>
            {
                l.PermitLimit = s.ConcurrentRequests;
                l.QueueLimit = 0;
            });

            // 5. Per-client partition: one bucket per caller, so one noisy client cannot starve the others.
            o.AddPolicy(PerClient, ctx => RateLimitPartition.GetFixedWindowLimiter(ClientKey(ctx), _ =>
                new FixedWindowRateLimiterOptions { PermitLimit = s.Permits, Window = s.Window, QueueLimit = 0 }));
        });

    /// <summary>
    /// Partition key: authenticated subject first, then remote IP. Call UseRateLimiter AFTER UseAuthentication,
    /// otherwise every caller is anonymous and shares one bucket. Behind a proxy, enable ForwardedHeaders first
    /// so RemoteIpAddress is the client, not the proxy.
    /// </summary>
    public static string ClientKey(HttpContext ctx) =>
        ctx.User.FindFirstValue("sub") is { Length: > 0 } sub ? "sub:" + sub
        : ctx.Connection.RemoteIpAddress is { } ip ? "ip:" + ip
        : "anonymous";

    private static async ValueTask WriteRejection(OnRejectedContext ctx, CancellationToken ct)
    {
        // Retry-After is only known for limiters that can compute it (window, bucket); the concurrency limiter cannot.
        var seconds = ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry)
            ? Math.Max(1, (int)Math.Ceiling(retry.TotalSeconds))
            : 1;
        ctx.HttpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await ctx.HttpContext.Response.WriteAsJsonAsync(new
        {
            type = "https://www.rfc-editor.org/rfc/rfc9110#section-15.5.30",
            title = "Too many requests",
            status = 429,
        }, ct);
    }
}
