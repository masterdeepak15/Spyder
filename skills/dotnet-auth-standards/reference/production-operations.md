# Production operations guidelines for C# / ASP.NET Core

Scope: rate limiting, audit logging, production logging, observability (OpenTelemetry, health probes), scalability and resilience, and extensibility (options, DI, feature flags, versioning). Security controls are in `security-hardening.md`; HTTP semantics and the `429` status in `http-rest.md`; rate-limit header drafts and API versioning in `api-design.md`; general logging and DI style in `coding-standards.md`; TLS in `transport-tls.md`.

**Sources.** Read from downloaded text in `_sources/ops/` (data only): Microsoft's rate-limiting middleware article (algorithms, partitions, `OnRejected`, multi-instance note), "Build resilient HTTP apps" (standard resilience handler defaults), health checks (readiness versus liveness), generic host (shutdown order, 30 s default timeout), OpenTelemetry observability overview, options, logging, Kestrel options; the OWASP API Security 2023 API4 page; the `draft-ietf-httpapi-ratelimit-headers-12` text in `_sources/api/`. **Not read:** W3C Trace Context, OpenTelemetry specs, OWASP logging cheat sheets, NIST SP 800-92, retention laws, Polly docs beyond the Microsoft article, `Microsoft.Extensions.Compliance.Redaction`, feature-management libraries. Such statements are labelled **(author's recommendation)** or **(not read)**. **(verified)** means observed on a throwaway ASP.NET Core 10 app (SDK 10.0.401, Windows, `Microsoft.Extensions.Http.Resilience` 11.0.1, OpenTelemetry packages 1.21.0 restored; run with curl, then deleted; `TreatWarningsAsErrors` build succeeded). The samples `RateLimiting.cs`, `AuditLog.cs`, `SafeLogging.cs`, `Observability.cs`, `Resilience.cs` in `examples/ProductionSamples/` build with 0 warnings and `dotnet run -- selftest` passes (157 checks, including rate limiting, masking, `[LoggerMessage]`, OpenTelemetry in-memory export, health probes and resilience behaviour); where a block and a sample differ, the sample wins. Blocks still marked **unverified** have no sample or test (the audit hash chain, `BindConfiguration` options validation).

---

## 1. Rate limiting

Purpose: availability (OWASP API4 Unrestricted Resource Consumption) and abuse control on sensitive flows (API6, credential endpoints). OWASP's prevention advice: limit how often a client may call, fine-tune per business need because some endpoints need stricter policies, and throttle single operations such as OTP validation or password recovery.

### 1.1 The four built-in limiters

| Limiter | Registration | Limits | Use when | Retry-After from the limiter |
|---|---|---|---|---|
| Fixed window | `AddFixedWindowLimiter` | N requests per window; resets at the window boundary | Simple quotas, login attempts | **(verified)** yes (`10` for a 10 s window) |
| Sliding window | `AddSlidingWindowLimiter` | N requests per window, window moves by segment | Smoother than fixed; avoids a double burst at a window edge | **(verified)** none in my run: `429` without `Retry-After` |
| Token bucket | `AddTokenBucketLimiter` | Bucket of tokens, refilled by a fixed amount per period | Allow bursts, cap the average rate | **(verified)** yes (`5` with a 5 s period) |
| Concurrency | `AddConcurrencyLimiter` | N requests **in flight**; no time-based cap | Protect expensive endpoints (reports, exports, downstream-bound calls) | **(verified)** none: `429` without `Retry-After` (Microsoft also says it provides no `RetryAfter` metadata) |

Microsoft's guidance: fixed, sliding and token bucket cap requests per period; the concurrency limiter caps only concurrent requests. Choose by the cost of the endpoint (time, CPU, I/O, data access). **Chooser (author's recommendation):** credential and OTP endpoints: fixed or sliding window per account and per IP; general API: token bucket per client or user; expensive or slow endpoints: concurrency, optionally combined with a window limiter (chain with `RateLimiter.CreateChained` inside a named policy, per the article).

### 1.2 Configuration, partitioning, per-endpoint policies, 429 and Retry-After

```csharp
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.OnRejected = (ctx, ct) =>
    {
        if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var ra))
            ctx.HttpContext.Response.Headers.RetryAfter =
                ((long)Math.Ceiling(ra.TotalSeconds)).ToString(NumberFormatInfo.InvariantInfo);
        return ValueTask.CompletedTask;
    };
    o.AddFixedWindowLimiter("fixed", c => { c.PermitLimit = 2; c.Window = TimeSpan.FromSeconds(10); c.QueueLimit = 0; });
    o.AddSlidingWindowLimiter("sliding", c => { c.PermitLimit = 2; c.Window = TimeSpan.FromSeconds(10); c.SegmentsPerWindow = 5; c.QueueLimit = 0; });
    o.AddTokenBucketLimiter("bucket", c => { c.TokenLimit = 2; c.TokensPerPeriod = 1; c.ReplenishmentPeriod = TimeSpan.FromSeconds(5); c.QueueLimit = 0; });
    o.AddConcurrencyLimiter("conc", c => { c.PermitLimit = 1; c.QueueLimit = 0; });
    o.AddPolicy("per-user", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.User.Identity?.Name ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 2, Window = TimeSpan.FromSeconds(10) }));
});

app.UseRouting();
app.UseRateLimiter();                                   // after UseRouting when endpoint policies are used
app.MapGet("/fixed", () => "ok").RequireRateLimiting("fixed");
```

**(verified)** this compiled with `TreatWarningsAsErrors` (usings: `System.Globalization`, `System.Threading.RateLimiting`, `Microsoft.AspNetCore.RateLimiting`) and behaved as follows with three requests per endpoint: `200, 200, 429` for fixed, sliding, bucket and the `per-user` policy; a second request to the concurrency endpoint while the first was still running got `429`. The `Retry-After` column above comes from the same run.

Rules:
1. **Partition.** An unpartitioned limiter is one shared bucket: one abusive client exhausts it for everybody. Partition by authenticated user or client id first, then API key, then IP address. IP-based keys behave correctly only if forwarded headers are configured for known proxies (`transport-tls.md`); behind NAT many users share one IP.
2. **Authenticate before partitioning by user.** The limiter reads `ctx.User`; with `UseRateLimiter` before `UseAuthentication` the user is anonymous. Put the limiter after authentication when partitioning by identity, and keep a stricter anonymous or IP limiter in front for credential endpoints **(author's recommendation; pipeline order is also listed in `api-design.md`)**.
3. **Per-endpoint policies** with `RequireRateLimiting("name")` or `[EnableRateLimiting]`/`[DisableRateLimiting]` on controllers; use a global limiter as a safety net.
4. **Respond with `429 Too Many Requests`** (RFC 6585, see `http-rest.md`) and `Retry-After` in delay-seconds when the limiter supplies it; never invent a value. Add a Problem Details body (`http-rest.md`) **(author's recommendation)**. The `RateLimit-*` response header fields are an Internet-Draft (`draft-ietf-httpapi-ratelimit-headers`), not an RFC; label them as such if you emit them (`api-design.md`).
5. **Do not use queueing** (`QueueLimit > 0`) for interactive APIs unless you want requests to wait; queued requests hold connections.
6. **Limit counters live in process memory.** Microsoft states each app instance enforces its limits separately, so the effective limit is the configured limit times the number of instances (100 per minute on three instances allows up to 300). The documented remedies are a shared layer such as an API gateway, or a custom limiter that stores counters in a shared store.
7. **Distributed option (author's recommendation; no package was installed or run here):** enforce the real quota at the gateway or ingress (Azure API Management, YARP with a limiter, NGINX, Envoy), and keep the in-process limiter as a per-instance backstop. If the quota must be exact and per-user across instances, implement a `PartitionedRateLimiter` whose leases call Redis (atomic increment plus expiry in one Lua script, or a token-bucket script), and decide fail-open versus fail-closed when Redis is unreachable (fail-open for general APIs, fail-closed for credential endpoints). Build, load-test and monitor this yourself; the middleware has no built-in Redis limiter.
8. **Metrics**: the middleware emits metrics under the meter `Microsoft.AspNetCore.RateLimiting` (the article has a metrics section; the name was registered in the probe, not checked against emitted data). Alert on sustained rejection rates.

---

## 2. Audit logging

Audit logs answer "who did what to which object, when, from where, and with what result" for security review, incident response and compliance. They are different from application logs: different audience, retention and integrity needs.

Rules **(author's recommendation; no standard or law was read for this section)**:
1. **Record security-relevant events**: sign-in success and failure, token issuance and refresh failures, logout, password or MFA change, authorization denials (401/403), privilege and role changes, creation/reading/changing/deletion of sensitive data, administrative actions, configuration and key changes, export of data, webhook endpoint changes.
2. **Record the actor and the object**, not the data: actor id (the token `sub`, not the name), tenant, client id, action, resource type and id, outcome (success/denied/error), reason code, correlation and trace id, source IP, user agent, timestamp in UTC. Store **before/after identifiers or changed field names**, not full personal data.
3. **Never write** secrets, tokens, passwords, full card numbers, or raw request bodies. Mask or hash personal data (email to a salted hash or partial mask) when the event needs to reference it; keep the mapping in the primary data store with its own access control.
4. **Separate sink from application logs.** Application logs are for debugging, can be sampled and dropped, and are widely readable; audit events must not be sampled, need longer retention and restricted read access. Use a separate table, stream or log pipeline with append-only permissions for the app (the app identity can insert, not update or delete).
5. **Write reliably.** Write the audit record in the same transaction as the business change, or use an outbox, so a change cannot happen without its audit record. Decide per event whether an audit failure must fail the request (it should for privileged actions).
6. **Tamper evidence.** Options: an append-only or WORM store; hash chaining (each record stores the hash of the previous record plus its own fields, so edits or deletions break the chain; keep periodic chain heads somewhere the application cannot write); signed batches; forwarding to an external SIEM. Hash chaining detects tampering but does not prevent it.
7. **Retention.** Set a period from your legal and business requirements (not from this guide), document it, enforce deletion, and keep personal data in audit records minimal so deletion requests remain possible.
8. **Time**: use `TimeProvider` (UTC) so tests control it; ensure host clocks are synchronized.
9. Audit access to the audit store.

Suggested schema (JSON or table columns): `id`, `timestampUtc`, `eventType`, `actorId`, `actorType`, `tenantId`, `clientId`, `action`, `resourceType`, `resourceId`, `outcome`, `reasonCode`, `sourceIp`, `userAgentHash`, `traceId`, `correlationId`, `prevHash`, `hash`, `schemaVersion`.

```csharp
public sealed record AuditEvent(
    string EventType, string ActorId, string Action, string ResourceType, string ResourceId,
    string Outcome, string? ReasonCode, string? TraceId, DateTimeOffset TimestampUtc);

public interface IAuditSink { ValueTask WriteAsync(AuditEvent e, CancellationToken ct); }
```

This type block is shape only (not compiled as written). `examples/ProductionSamples/AuditLog.cs` compiles and tests the masking (email, IP truncation), the dedicated `Audit` logger category, event id 9000 and log-forging resistance **(verified, selftest)**. It does **not** implement a hash chain, so the tamper-evident chain stays **unverified**: treat it as a design idea (append-only sink, or a sink with its own integrity guarantees) until someone builds and tests it.

---

## 3. Production logging

Rules:
1. **Structured logging with message templates.** `logger.LogInformation("Order {OrderId} shipped", id)`, never `$"..."` or string concatenation (`coding-standards.md` section 4.7). Property names are stable fields for queries.
2. **High-performance paths: the `[LoggerMessage]` source generator.** A `static partial` method with the attribute produces a cached, allocation-light delegate and checks `IsEnabled` first. Microsoft's logging article recommends it for hot paths; the same attribute form is compiled in `examples/ProductionSamples/SafeLogging.cs` and its selftest checks that the generated method writes the event id and template values and that an injected newline cannot add a log line **(verified)**; this block itself is a shorter variant of that code:

```csharp
public static partial class Log
{
    [LoggerMessage(EventId = 1001, Level = LogLevel.Warning, Message = "Login failed for client {ClientId}")]
    public static partial void LoginFailed(ILogger logger, string clientId);
}
```

3. **Levels.** Do not enable Trace/Debug in production: Microsoft notes Trace messages may contain sensitive data and should not be enabled in production. Set levels by category in configuration (`Logging:LogLevel`), lower noisy categories, and filter health-check request logs.
4. **Redact secrets and PII.** Never log tokens, `Authorization` headers, passwords, cookies, full request/response bodies, connection strings or full personal records. Use `[LogProperties]` and the redaction packages in `Microsoft.Extensions.Compliance.*` **(not read; check current docs)** or a small own helper that masks (`a***@example.com`). Log ids, not objects; avoid `{@Order}` style object destructuring of entities that hold personal data. Enable `HttpLogging` fields selectively, never bodies, in production **(author's recommendation)**.
5. **Log injection prevention.** User-controlled text containing CR/LF can forge extra log lines. Structured sinks (JSON) escape values, but console and text sinks do not. Strip or replace control characters before logging user text. **(verified)** `Regex.Replace(q, "[\r\n]", "_")` turned `a%0D%0AFAKE entry` into the single log line `search a__FAKE entry`. A stricter form removes all control characters (`char.IsControl`) **(author's recommendation)**. Also never use user text as the message template.
6. **Correlation.** ASP.NET Core and `HttpClient` create `Activity` objects that follow W3C Trace Context (`traceparent` header) **(not read in the specification; behaviour as used by the OpenTelemetry instrumentation)**. Include `TraceId`/`SpanId` in logs by setting `ActivityTrackingOptions` on the logger factory or using the OpenTelemetry logging provider, and return the trace id in Problem Details extensions for support. A custom `X-Correlation-Id` is only needed for non-W3C callers; map it into the activity or the log scope instead of keeping two ids.
7. **Exceptions**: log once, at the boundary, with the exception object (`logger.LogError(ex, "...")`); do not log and rethrow at each layer (`coding-standards.md` section 4.5).
8. **Do not use logs for audit** (section 2) or for metrics.

---

## 4. Observability

### 4.1 OpenTelemetry

Microsoft's overview: observability uses logs, metrics and traces; .NET exposes them through `ILogger`, `Meter` (metrics) and `Activity` (traces), and OpenTelemetry (OTel) is the vendor-neutral way to export them, usually over OTLP.

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation())
    .WithMetrics(m => m.AddAspNetCoreInstrumentation().AddMeter("Microsoft.AspNetCore.RateLimiting"));
// export: add .UseOtlpExporter() (OpenTelemetry.Exporter.OpenTelemetryProtocol) with OTEL_EXPORTER_OTLP_ENDPOINT set
```

**(verified)** the registration above compiled and the app ran with packages `OpenTelemetry.Extensions.Hosting`, `.Instrumentation.AspNetCore`, `.Instrumentation.Http` and `.Exporter.OpenTelemetryProtocol` (all 1.21.0). **Not verified:** that spans or metrics reached a collector, and the `UseOtlpExporter` call (it is in a comment). Rules:
1. Name the service (`OTEL_SERVICE_NAME` or `ConfigureResource`), propagate context on outbound calls (HttpClient instrumentation), and add your own `ActivitySource`/`Meter` for business operations.
2. **Do not put PII or secrets in span attributes, baggage or metric labels.** Avoid high-cardinality metric labels (user id, full URL).
3. **Sampling**: sample traces in production (head or tail), never sample audit events (section 2).
4. Keep telemetry endpoints and collectors on an internal network; protect OTLP with TLS and auth.
5. Alert on the signals that matter: error rate, latency percentiles, saturation (queue depth, rate-limit rejections, circuit breaker state), dependency failures.

### 4.2 Health, readiness and liveness probes

Microsoft's distinction: **liveness** says the process has crashed and must be restarted; **readiness** says the app is running but may not be ready for traffic (for example still loading configuration). A liveness probe should run no dependency checks, so an outage of a database does not make the orchestrator restart healthy pods.

```csharp
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddCheck("dep", () => HealthCheckResult.Healthy(), tags: ["ready"]);
app.MapHealthChecks("/health/live", new() { Predicate = r => r.Tags.Contains("live") });
app.MapHealthChecks("/health/ready", new() { Predicate = r => r.Tags.Contains("ready") });
```

**(verified)** both endpoints returned `200` with body `Healthy`. Rules: readiness checks real dependencies with short timeouts; liveness stays cheap; expose probes on a separate internal port or protect them (`RequireHost`/`RequireAuthorization`), because detailed output leaks topology; do not return detailed exception text on public probes; consider a startup probe for slow initialization; filter probe requests out of access logs and traces.

---

## 5. Scalability and resilience

1. **Stateless services.** Keep no user state in process memory: sessions, caches that must be shared, rate counters, idempotency records, SignalR/MCP session state. Scale-out then needs only a load balancer. Data-protection keys must be shared across instances if you use cookies/antiforgery (persist them to a shared store) **(author's recommendation)**.
2. **Distributed cache** (`IDistributedCache`, Redis or SQL; `HybridCache` in .NET 9+ combines in-process and distributed layers and protects against stampedes) for shared state; set expirations and treat the cache as a hint, not the source of truth **(HybridCache and Redis behaviour not read or run here)**.
3. **Resilient outbound HTTP.** Use `Microsoft.Extensions.Http.Resilience`. `AddStandardResilienceHandler()` stacks (per Microsoft's table) a rate limiter, total request timeout, retry (max 3 retries, exponential backoff with jitter, 2 s base delay), circuit breaker and attempt timeout (10 s). Retries apply to all HTTP methods by default; Microsoft warns this can duplicate a POST that inserts a record, and offers `DisableForUnsafeHttpMethods()` (POST, PATCH, PUT, DELETE, CONNECT). Combine unsafe-method retries only with idempotency keys (`http-rest.md`).

```csharp
builder.Services.AddHttpClient("dep").AddStandardResilienceHandler(o =>
{
    o.Retry.MaxRetryAttempts = 2;
    o.Retry.DisableForUnsafeHttpMethods();
    o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(2);
    o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(8);
    o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(10);   // must cover at least twice the attempt timeout
});
```

**(verified)** compiled with `using Microsoft.Extensions.Http.Resilience;` (without the using, `DisableForUnsafeHttpMethods` is not found: CS1061). **Not verified:** the handler's runtime behaviour in my run (no failing dependency was called). The comment on `SamplingDuration` is the author's memory of option validation and was **not** tested; if startup validation complains, adjust the values. Set timeouts on every outbound call; budget them so total timeout is above attempts times attempt timeout plus backoff, and below the caller's own timeout. For circuit breakers, decide what callers see when open (`503` with `Retry-After`, a cached value, or a degraded response). Hedging (`AddStandardHedgingHandler`) is for idempotent reads against replicated endpoints only.
4. **Cancellation everywhere**: pass `HttpContext.RequestAborted`/`CancellationToken` to I/O (`coding-standards.md` section 4.6).
5. **Background jobs.** Use `BackgroundService` for in-process loops; for work that must survive restarts, retry, or run once across instances, use a queue plus workers or a job scheduler with a lease/lock (Hangfire, Quartz, a cloud queue). Make handlers idempotent because delivery is at-least-once. Do not do long work inside request handlers.
6. **Graceful shutdown.** Microsoft's generic-host article: on `SIGTERM`/Ctrl+C the host signals `ApplicationStopping`, then stops the server (Kestrel stops accepting connections; HTTP/2 and HTTP/3 clients get a `GOAWAY`), with a shutdown timeout that defaults to **30 s**, then calls `IHostedService.StopAsync` in reverse order. In Kubernetes, make `terminationGracePeriodSeconds` larger than the host shutdown timeout; fail readiness first so the load balancer drains **(author's recommendation)**; honor the `CancellationToken` passed to `ExecuteAsync`/`StopAsync`.
7. **Backpressure**: bounded queues (`Channel.CreateBounded`), concurrency limits and rate limiting protect you from your own callers; return `429`/`503` early rather than collapse.

---

## 6. Extensibility

1. **Options pattern.** Bind typed options from configuration and validate at startup. Microsoft: plain `IOptions<T>` does not see changes after start; use `IOptionsSnapshot<T>` (scoped, recomputed per request) or `IOptionsMonitor<T>` (singleton, change notifications) when values reload.

```csharp
builder.Services.AddOptions<PaymentsOptions>()
    .BindConfiguration("Payments")
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

Unverified (not compiled). `BindConfiguration` needs `Microsoft.Extensions.Options.ConfigurationExtensions`, normally present in ASP.NET Core projects.
2. **Dependency injection**: depend on abstractions at real boundaries (clock, HTTP clients, stores), register by lifetime (`coding-standards.md` section 4.4), and use named/typed `HttpClient`s. Extension points: `IServiceCollection` extension methods per feature (`AddPayments()`), decorators for cross-cutting concerns, keyed services for variants.
3. **Feature flags.** Separate deploy from release. Simple flags: options bound from configuration, reloadable with `IOptionsMonitor`. Richer needs (percentage rollouts, targeting): `Microsoft.FeatureManagement` or a vendor SDK **(not read or installed)**. Rules: flags have an owner and an expiry, default to the safe (off) state when the store is unreachable, are evaluated on the server, and security checks never depend on a flag the caller can influence. Test both states.
4. **API versioning.** Choose one scheme (URL segment, header or media type), keep old versions until a published sunset, announce deprecation with the `Deprecation` and `Sunset` headers (RFC 9745 and RFC 8594, covered in `api-design.md`), and keep one OpenAPI document per version. Prefer additive, backwards-compatible changes; never change the meaning of an existing field. A package such as `Asp.Versioning.Http` implements the routing **(not installed here)**.

---

## 7. Checklist

Rate limiting
- [ ] Limiter type chosen for the endpoint cost; credential/OTP/business-flow endpoints have their own stricter policy.
- [ ] Partitioned by user, client, key or IP (not one global bucket); forwarded headers configured when keying by IP.
- [ ] Limiter placed after routing (and after authentication when keyed by user); `429` plus `Retry-After` only where the limiter provides it; `RateLimit-*` headers labelled as a draft.
- [ ] Multi-instance behaviour decided: gateway or shared-store limiter, or the per-instance multiplication accepted and documented.

Audit logging
- [ ] Security-relevant events recorded with actor, object, outcome, time (UTC), source and trace id; no secrets or raw personal data.
- [ ] Separate sink with append-only access, not sampled, tamper evidence and a documented retention period.
- [ ] Audit write is atomic with the business change (or outbox) for privileged actions.

Logging
- [ ] Message templates, no string interpolation; `[LoggerMessage]` on hot paths.
- [ ] Redaction of tokens, secrets and personal data; Trace/Debug off in production; CR/LF removed from user text.
- [ ] Trace id in logs and in error responses; exceptions logged once.

Observability
- [ ] OpenTelemetry traces and metrics registered with a service name and an exporter; no PII in attributes or labels; sampling decided.
- [ ] Separate liveness (no dependencies) and readiness (dependencies) probes, not exposed publicly with detail.

Scalability and resilience
- [ ] No per-instance state that must be shared; shared cache or store where needed; data-protection keys shared.
- [ ] Outbound calls have timeouts, bounded retries with jitter, a circuit breaker; retries disabled for unsafe methods unless idempotent.
- [ ] Background work is idempotent, cancellable, and drains on shutdown within the configured timeout; grace period exceeds the host timeout.

Extensibility
- [ ] Options are typed, validated on start; reload semantics (`IOptionsMonitor`/`Snapshot`) chosen deliberately.
- [ ] Feature flags have an owner, an expiry and a safe default; no security decision depends on a client-controlled flag.
- [ ] One versioning scheme; deprecation and sunset communicated; one OpenAPI document per version.
