# Code-review checklist: Security hardening and production operations

For reviewing C# / ASP.NET Core changes. Each item points to the reference section with the rule and example, and to the RFC or specification. Mark an item N/A only if the code does not touch that area. Short rules: see the matching file in `../reference/` (routing in `../SKILL.md`).

Reference files: [S] `../reference/security-hardening-details.md`, [P] `../reference/production-operations-details.md`.

## 11. Security hardening and production operations

Sources: OWASP API Security Top 10 2023 and Microsoft documentation named in [S] and [P]; items marked "author" are the guide author's recommendations, not standards. Most code in those guides is covered by the `examples/ProductionSamples` selftest; the few blocks without a sample or test (audit hash chain, `BindConfiguration` options, Kestrel limits, fallback policy, Key Vault, CI job) are still labelled unverified there.

- [ ] Every id-taking endpoint checks the caller's right to the object; every endpoint has a policy or explicit `AllowAnonymous` (fallback policy set). [S §1, §1.2](../reference/security-hardening-details.md#1-owasp-api-security-top-10-2023-mapped-to-aspnet-core), OWASP API1, API5
- [ ] Request, header, upload and page sizes and timeouts are bounded. [S §1.1](../reference/security-hardening-details.md#11-api4-set-limits-explicitly), OWASP API4
- [ ] No secrets in source, `appsettings*.json`, images or logs; user secrets in development, vault in production; required secrets validated at start. [S §2](../reference/security-hardening-details.md#2-secrets-management), Microsoft app-secrets article
- [ ] Server-side allow-list validation, parameterised queries, no user-built paths, URLs, JSON or HTML. [S §3](../reference/security-hardening-details.md#3-input-validation-and-output-encoding), OWASP API3, API10
- [ ] Security headers set (`nosniff`, CSP, `Referrer-Policy`, HSTS, `Cache-Control` for sensitive data); `Server` header off; CORS allow-list and CSRF protection when cookies are used. [S §4, §5](../reference/security-hardening-details.md#5-security-headers), author
- [ ] CI fails on vulnerable packages and runs SAST, secret scanning, SBOM and container scan; lock files and package source mapping used. [S §6](../reference/security-hardening-details.md#6-dependency-and-supply-chain-scanning-in-ci), author
- [ ] No developer exception page, dev certificate or open Swagger UI in production; non-root container. [S §7](../reference/security-hardening-details.md#7-secure-defaults-for-the-host), OWASP API8
- [ ] Rate limiter type fits the endpoint, is partitioned (not one global bucket), strict on credential and business-flow endpoints, returns `429` with `Retry-After` when available. [P §1.1](../reference/production-operations-details.md#11-the-four-built-in-limiters), Microsoft rate limiting, OWASP API4, API6
- [ ] Multi-instance rate limiting decided (gateway or shared store), because counters are per instance. [P §1.2](../reference/production-operations-details.md#12-configuration-partitioning-per-endpoint-policies-429-and-retry-after), Microsoft rate limiting
- [ ] Audit events record actor, object, outcome, UTC time and trace id with no secrets; separate append-only sink; tamper evidence; retention set; written atomically with the change. [P §2](../reference/production-operations-details.md#2-audit-logging), author
- [ ] Logging uses templates (`[LoggerMessage]` on hot paths), redacts secrets and personal data, strips CR/LF from user text, has no Trace/Debug in production, carries trace id. [P §3](../reference/production-operations-details.md#3-production-logging), Logging in .NET
- [ ] OpenTelemetry traces and metrics with a service name; no PII in attributes or labels; separate liveness and readiness probes not exposed with detail. [P §4](../reference/production-operations-details.md#4-observability), Microsoft health checks, OpenTelemetry overview
- [ ] Outbound calls use timeouts, bounded jittered retries and a circuit breaker; retries off for unsafe methods unless idempotent; background work idempotent and drains within the shutdown timeout. [P §5](../reference/production-operations-details.md#5-scalability-and-resilience), Microsoft HTTP resilience, generic host
- [ ] Options typed and validated on start; feature flags have owner, expiry and safe default; one versioning scheme with deprecation signalled. [P §6](../reference/production-operations-details.md#6-extensibility), Options pattern, RFC 9745, RFC 8594

## How to report

For each finding give: file and line, the item above, the RFC section, and the fix. Do not claim an item passes without reading the code or running a test for it.
