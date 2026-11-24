using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace ProductionSamples;

/// <summary>
/// Outbound resilience with Microsoft.Extensions.Http.Resilience (built on Polly v8).
/// Order matters, outermost first: total timeout -> retry -> circuit breaker -> per-attempt timeout.
/// </summary>
public static class Resilience
{
    public const string ClientName = "downstream";

    public sealed record Settings(
        TimeSpan TotalTimeout, TimeSpan AttemptTimeout, int MaxRetries, TimeSpan RetryDelay,
        double BreakerFailureRatio, int BreakerMinThroughput, TimeSpan BreakerSampling, TimeSpan BreakerBreakDuration)
    {
        public static Settings Defaults { get; } = new(
            TotalTimeout: TimeSpan.FromSeconds(30), AttemptTimeout: TimeSpan.FromSeconds(5),
            MaxRetries: 3, RetryDelay: TimeSpan.FromMilliseconds(500),
            BreakerFailureRatio: 0.5, BreakerMinThroughput: 10,
            BreakerSampling: TimeSpan.FromSeconds(30), BreakerBreakDuration: TimeSpan.FromSeconds(15));
    }

    public static IHttpResiliencePipelineBuilder AddDownstreamClient(this IServiceCollection services, Settings s) =>
        services.AddHttpClient(ClientName, c => c.BaseAddress = new Uri("https://downstream.example.test"))
            .AddResilienceHandler("downstream-pipeline", builder =>
            {
                // 1. Total budget for the whole call including all retries. The caller never waits longer than this.
                builder.AddTimeout(s.TotalTimeout);

                // 2. Retry transient failures only: 5xx, 408, 429, HttpRequestException, timeouts (the default ShouldHandle).
                var retry = new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = s.MaxRetries,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,                      // avoids synchronized retry storms
                    Delay = s.RetryDelay,
                    ShouldRetryAfterHeader = true,         // honour Retry-After from the downstream
                };
                retry.DisableForUnsafeHttpMethods();       // POST/PATCH/DELETE may have taken effect: no blind retry without an Idempotency-Key
                builder.AddRetry(retry);

                // 3. Circuit breaker: stop hammering a dependency that is down; fail fast while open.
                builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    FailureRatio = s.BreakerFailureRatio,
                    MinimumThroughput = s.BreakerMinThroughput,
                    SamplingDuration = s.BreakerSampling,
                    BreakDuration = s.BreakerBreakDuration,
                });

                // 4. Timeout per attempt, so one hung connection does not eat the whole budget.
                builder.AddTimeout(s.AttemptTimeout);
            });
}
