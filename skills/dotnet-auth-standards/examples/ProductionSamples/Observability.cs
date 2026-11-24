using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace ProductionSamples;

/// <summary>Application-owned telemetry sources. Register the names below with OpenTelemetry or nothing is exported.</summary>
public static class AppTelemetry
{
    public const string Name = "ProductionSamples";
    public static readonly ActivitySource Source = new(Name);
    public static readonly Meter Meter = new(Name);
    public static readonly Counter<long> OrdersCreated = Meter.CreateCounter<long>("orders.created", description: "Orders accepted");
}

/// <summary>Switch for the readiness check; stands in for a database or queue connection.</summary>
public sealed class DependencyState
{
    public volatile bool DatabaseUp = true;
}

public static class Observability
{
    /// <summary>
    /// Tracing and metrics with OpenTelemetry. The exporter is chosen by configuration:
    /// OTEL_EXPORTER_OTLP_ENDPOINT set -> OTLP. Tests add in-memory exporters via the two callbacks.
    /// </summary>
    public static IServiceCollection AddSampleObservability(this IServiceCollection services, string serviceName,
        bool useOtlp, Action<TracerProviderBuilder>? tracing = null, Action<MeterProviderBuilder>? metrics = null)
    {
        var otel = services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName))
            .WithTracing(t =>
            {
                t.AddAspNetCoreInstrumentation()
                 .AddHttpClientInstrumentation()
                 .AddSource(AppTelemetry.Name);
                tracing?.Invoke(t);
            })
            .WithMetrics(m =>
            {
                m.AddAspNetCoreInstrumentation()
                 .AddHttpClientInstrumentation()
                 .AddRuntimeInstrumentation()
                 .AddMeter(AppTelemetry.Name);
                metrics?.Invoke(m);
            });

        if (useOtlp) otel.UseOtlpExporter();      // endpoint, headers and protocol come from OTEL_EXPORTER_OTLP_* settings
        return services;
    }

    /// <summary>
    /// Liveness answers "is the process alive" and checks NO dependency (a DB outage must not restart pods).
    /// Readiness answers "may I receive traffic" and checks dependencies.
    /// Responses are bare status words: health detail is reconnaissance for an attacker.
    /// </summary>
    public static IServiceCollection AddSampleHealthChecks(this IServiceCollection services)
    {
        services.AddSingleton<DependencyState>();
        services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
            .AddCheck<DatabaseCheck>("database", tags: ["ready"]);
        return services;
    }

    public static IEndpointRouteBuilder MapSampleHealth(this IEndpointRouteBuilder app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = r => r.Tags.Contains("live") });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = r => r.Tags.Contains("ready") });
        return app;
    }

    private sealed class DatabaseCheck(DependencyState state) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default) =>
            Task.FromResult(state.DatabaseUp ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("db down"));
    }
}
