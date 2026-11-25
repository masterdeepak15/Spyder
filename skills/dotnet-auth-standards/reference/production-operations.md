# Production operations rules for C# / ASP.NET Core

Must-follow rules only. Full limiter table, code blocks, audit schema, sourcing and what was verified: [production-operations-details.md](production-operations-details.md) (section numbers match it). Samples: `examples/ProductionSamples` (`RateLimiting.cs`, `AuditLog.cs`, `SafeLogging.cs`, `Observability.cs`, `Resilience.cs`; selftest passes). Unverified: audit hash chain, `BindConfiguration` options block. Items tagged (author) are recommendations, not standards.

## 1. Rate limiting ([§1](production-operations-details.md#1-rate-limiting))
- Pick the limiter by endpoint cost: fixed or sliding window for credential/OTP endpoints, token bucket for general API (bursts), concurrency for expensive endpoints. `Retry-After` comes only from fixed and token bucket in the verified run; never invent a value ([§1.1](production-operations-details.md#11-the-four-built-in-limiters)).
- Partition by user or client id, then API key, then IP; never one global bucket. IP keys need forwarded headers from known proxies.
- Limiter after routing, and after authentication when keyed by user; keep a stricter anonymous/IP limiter in front of credential endpoints.
- Per endpoint: `RequireRateLimiting("name")` or `[EnableRateLimiting]`; a global limiter as safety net.
- Respond `429` (`RejectionStatusCode`) with `Retry-After` when available and a Problem Details body (author). `RateLimit-*` headers are an Internet-Draft: label them.
- `QueueLimit = 0` for interactive APIs.
- Counters are per instance: the effective limit is limit x instances. Enforce the real quota at a gateway or shared store; decide fail-open (general) or fail-closed (credential) when the store is down. Alert on sustained rejections (meter `Microsoft.AspNetCore.RateLimiting`).

## 2. Audit logging ([§2](production-operations-details.md#2-audit-logging), all author)
- Record sign-in success/failure, token issue/refresh failures, logout, password/MFA changes, 401/403 denials, role changes, sensitive data access/change/delete/export, admin, config and key changes, webhook endpoint changes.
- Record actor id (`sub`), tenant, client, action, resource type and id, outcome, reason code, trace id, source IP, UTC time. Identifiers or changed field names, not data. Never secrets, tokens, passwords, card numbers or raw bodies; mask or hash personal data.
- Separate sink from application logs: not sampled, append-only for the app identity, restricted read, longer retention; audit access to it.
- Write atomically with the business change (or outbox); privileged actions fail if audit fails.
- Tamper evidence (WORM store, hash chain, signed batches, SIEM): a chain detects, does not prevent; unverified here.
- Retention from legal/business requirements, enforced; minimal personal data. Use `TimeProvider` (UTC).

## 3. Production logging ([§3](production-operations-details.md#3-production-logging))
- Message templates with named placeholders, never `$""`; `[LoggerMessage]` on hot paths.
- No Trace/Debug in production; levels by category; filter health-check request logs.
- Never log tokens, `Authorization`, passwords, cookies, bodies, connection strings, full personal records; log ids, not entity objects; mask helper or redaction packages (not read); `HttpLogging` without bodies.
- Log injection: strip CR/LF (better, all control characters) from user text; never use user text as the template.
- Trace id (W3C `traceparent`) in logs and in Problem Details for support; map `X-Correlation-Id` into the activity instead of a second id.
- Log an exception once, at the boundary. Logs are not audit and not metrics.

## 4. Observability ([§4](production-operations-details.md#4-observability))
- OpenTelemetry traces and metrics (ASP.NET Core and HttpClient instrumentation) with a service name; own `ActivitySource`/`Meter` for business operations. Export reaching a collector is unverified.
- No PII or secrets in span attributes, baggage or metric labels; avoid high-cardinality labels; sample traces, never audit events; TLS and auth on OTLP; alert on error rate, latency percentiles, saturation, rejections, breaker state.
- Probes: liveness is cheap with no dependency checks; readiness checks real dependencies with short timeouts. Probes are on an internal port or protected, with no detailed output; consider a startup probe; keep them out of access logs and traces ([§4.2](production-operations-details.md#42-health-readiness-and-liveness-probes)).

## 5. Scalability and resilience ([§5](production-operations-details.md#5-scalability-and-resilience))
- Stateless instances: no shared state in process memory (sessions, caches, counters, idempotency records, MCP/SignalR state); share data-protection keys across instances; distributed cache with expirations, never source of truth.
- Outbound HTTP: `AddStandardResilienceHandler` with `Retry.DisableForUnsafeHttpMethods()`, explicit attempt and total timeouts, circuit breaker; add the `Microsoft.Extensions.Http.Resilience` using. Unsafe-method retries only with idempotency keys. Total timeout above attempts x attempt timeout plus backoff and below the caller's timeout. Decide what callers see when the breaker is open. Hedging only for idempotent reads.
- Pass `HttpContext.RequestAborted`/`CancellationToken` to all I/O. Background work: `BackgroundService` or queue plus workers; handlers idempotent (at-least-once); no long work in request handlers.
- Graceful shutdown: host timeout defaults to 30 s; Kubernetes `terminationGracePeriodSeconds` larger; fail readiness first; honour the stopping token. Bounded queues and early `429`/`503` for backpressure.

## 6. Extensibility ([§6](production-operations-details.md#6-extensibility))
- Typed options validated on start (`ValidateDataAnnotations().ValidateOnStart()`); `IOptions` does not reload, use `IOptionsSnapshot` or `IOptionsMonitor` deliberately.
- Inject abstractions at real boundaries; `AddFeature()` extension methods per feature; decorators and keyed services for variants.
- Feature flags: owner, expiry, safe default (off) when the store is unreachable, server-side evaluation, no security check depends on a caller-influenced flag; test both states.
- One versioning scheme; announce deprecation with `Deprecation` and `Sunset` (RFC 9745, RFC 8594); one OpenAPI document per version; additive changes only.

## Review
Per-area checklist: [../checklists/security-ops.md](../checklists/security-ops.md) (shared with security hardening).
