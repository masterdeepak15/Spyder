# Code-review checklist: OAuth/OIDC, tokens, identity, REST, API design, webhooks, MCP, TLS, coding standards, security hardening, production operations

For reviewing C# / ASP.NET Core changes. Each item points to the reference section with the rule and example, and to the RFC or specification. Mark an item N/A only if the code does not touch that area.

Reference files: [O] `../reference/oauth-oidc.md`, [T] `../reference/tokens-jwt.md`, [H] `../reference/http-rest.md`, [A] `../reference/api-design.md`, [W] `../reference/webhooks-events.md`, [M] `../reference/mcp-ai.md`, [X] `../reference/transport-tls.md`, [C] `../reference/coding-standards.md`, [S] `../reference/security-hardening.md`, [P] `../reference/production-operations.md`.

## 1. OAuth 2.0 / 2.1 / OIDC flows

- [ ] Authorization code + PKCE `S256` for user flows; no implicit, no password grant. [O §1, §4, §5](../reference/oauth-oidc.md#5-rfc-9700-and-oauth-21), RFC 7636, RFC 9700
- [ ] Redirect URIs are exact strings compared ordinally; no wildcards. [O §5](../reference/oauth-oidc.md#5-rfc-9700-and-oauth-21), RFC 9700
- [ ] PKCE verifier from a CSPRNG, 43+ chars, fresh per request. [O §4](../reference/oauth-oidc.md#4-rfc-7636-pkce), RFC 7636
- [ ] No secrets in source or committed appsettings; public clients have no secret. [O §2](../reference/oauth-oidc.md#2-rfc-6749), RFC 6749 2.3
- [ ] Token endpoint errors are mapped from the `error` code, not message text; old refresh token replaced when a new one is issued. [O §2](../reference/oauth-oidc.md#2-rfc-6749), RFC 6749 5.2
- [ ] Public-client refresh tokens are rotated or DPoP-bound; logout calls revocation. [O §5, §10](../reference/oauth-oidc.md#10-rfc-7662-and-7009), RFC 9700, RFC 7009
- [ ] Downstream calls use client credentials or an exchanged token with the downstream `audience`/`resource`; the inbound token is never forwarded. [O §6, §9](../reference/oauth-oidc.md#9-rfc-8693-and-7523), RFC 8707, RFC 8693
- [ ] Discovery is used instead of hard-coded endpoints; well-known URL built by path insertion; `issuer`/`resource` compared for exact equality. [O §7](../reference/oauth-oidc.md#7-rfc-8414-and-9728), RFC 8414, RFC 9728
- [ ] Introspection callers check `active`, `aud` and `scope`; cache bounded by `exp`. [O §10](../reference/oauth-oidc.md#10-rfc-7662-and-7009), RFC 7662
- [ ] Device flow: interval honoured, `slow_down` adds 5 s, `user_code` brute force limited. [O §11](../reference/oauth-oidc.md#11-rfc-8628), RFC 8628
- [ ] DPoP: fresh `jti`/`iat`, RS verifies `ath`, `cnf.jkt` and replay; DPoP-bound tokens rejected as `Bearer`. [O §12](../reference/oauth-oidc.md#12-rfc-9449-dpop), RFC 9449
- [ ] Anything attributed to "OAuth 2.1" is labelled as an Internet-Draft; binding rules cite RFC 9700. [O §5](../reference/oauth-oidc.md#5-rfc-9700-and-oauth-21)

## 2. Bearer tokens on the API

- [ ] Token only in the `Authorization` header; no `access_token` query parameter; never logged. [O §3](../reference/oauth-oidc.md#3-rfc-6750), RFC 6750
- [ ] 401 with `WWW-Authenticate: Bearer` (and `error="invalid_token"` only when a token was sent and failed); 403 `insufficient_scope` for scope failures; `resource_metadata` advertised. [O §3, §7](../reference/oauth-oidc.md#3-rfc-6750), RFC 6750, RFC 9728
- [ ] Validation exception text is not echoed to the caller. [O §3](../reference/oauth-oidc.md#3-rfc-6750)
- [ ] `ValidateAudience = true`, `ValidAudience` is this API; tested with a token for another audience. [O §6](../reference/oauth-oidc.md#6-rfc-8707), RFC 8707
- [ ] Scope/role checks via a splitting helper and named policies, never `Contains` on the raw scope string. [T §5](../reference/tokens-jwt.md#5-rfc-9068-jwt-access-tokens), RFC 9068 2.2.3

## 3. JWT validation

- [ ] Issuer, audience, lifetime, signature and `RequireSignedTokens` explicitly set; none disabled. [T §1, §2](../reference/tokens-jwt.md#1-jwt-validation-pitfalls), RFC 7519 7.2, RFC 8725 3
- [ ] `ValidAlgorithms` is an allow-list of what the issuer signs with; no `none`; no HS* verified with a public key. [T §1](../reference/tokens-jwt.md#1-jwt-validation-pitfalls), RFC 8725 2.1, 3.1
- [ ] `ValidTypes` requires `at+jwt` for access tokens; ID tokens cannot be used as access tokens. [T §1, §5](../reference/tokens-jwt.md#5-rfc-9068-jwt-access-tokens), RFC 9068 2.1, 4
- [ ] Clock skew is a small value set in code (default is 5 min); `exp` required. [T §1](../reference/tokens-jwt.md#1-jwt-validation-pitfalls), RFC 7519 4.1.4
- [ ] Required claims are listed in code; claims are read only from the validated token. [T §2](../reference/tokens-jwt.md#2-rfc-7519-jwt), RFC 7519 11.1
- [ ] No manual JWT parsing for trust decisions; `jku`, `x5u`, `jwk`, `x5c` from tokens are not followed; `kid` is an opaque lookup key. [T §3](../reference/tokens-jwt.md#3-rfc-7515-jws), RFC 7515 4.1, RFC 8725 3.10
- [ ] HMAC keys, if any, are 256 random bits, not passwords; asymmetric preferred. [T §1](../reference/tokens-jwt.md#1-jwt-validation-pitfalls), RFC 8725 3.5
- [ ] JWKS comes from trusted metadata over HTTPS; unknown-`kid` refresh is rate-limited; rotation publishes the new key before use and keeps the old one until tokens expire. [T §4](../reference/tokens-jwt.md#4-rfc-7517-jwk-and-key-rotation), RFC 7517
- [ ] The JWKS endpoint serves public parameters only (automated test for `d`, `p`, `q`, `k`). [T §4](../reference/tokens-jwt.md#4-rfc-7517-jwk-and-key-rotation), RFC 7517 7
- [ ] Tokens carry no more personal data than the API needs. [T §5](../reference/tokens-jwt.md#5-rfc-9068-jwt-access-tokens), RFC 9068 6

## 4. Identity (OIDC, SCIM)

- [ ] OIDC authority is the exact issuer; `ResponseType = Code`, `UsePkce = true`; `nonce` generated and checked. [T §6](../reference/tokens-jwt.md#6-openid-connect-core), OIDC Core 3.1.3.7
- [ ] User key is `iss` + `sub`, never email or name. [T §6](../reference/tokens-jwt.md#6-openid-connect-core), OIDC Core 2
- [ ] UserInfo `sub` equals the ID token `sub` before the response is used. [T §6](../reference/tokens-jwt.md#6-openid-connect-core), OIDC Core 5.3.4
- [ ] ID token never sent to APIs; access token never parsed as identity; session lifetime decided independently of ID token `exp`. [T §6](../reference/tokens-jwt.md#6-openid-connect-core)
- [ ] Discovery `issuer` equals the URL used to fetch it; `RequireHttpsMetadata` true outside local development. [T §7](../reference/tokens-jwt.md#7-openid-connect-discovery), OIDC Discovery 4.3
- [ ] SCIM: TLS only, scopes mapped to read/write/admin, `application/scim+json`, SCIM error schema with string `status`. [T §8](../reference/tokens-jwt.md#8-rfc-7644-scim), RFC 7644 3.12, 7
- [ ] SCIM PATCH validated (schema URN, `Operations`, op names, paths); `startIndex`/`count` normalised and capped; filters parsed, never string-built into SQL/LDAP. [T §8](../reference/tokens-jwt.md#8-rfc-7644-scim), RFC 7644 3.4.2.4, 3.5.2

## 5. REST / HTTP semantics

- [ ] Methods match safe/idempotent semantics; no side effects on GET. [H §2](../reference/http-rest.md#2-methods-rfc-9110-section-9), RFC 9110 9
- [ ] Status codes: 201 + `Location`, 204 for empty success, 401 with `WWW-Authenticate`, 405 with `Allow`; never 200 for a failure. [H §3](../reference/http-rest.md#3-status-codes-that-matter-rfc-9110-section-15-rfc-6585), RFC 9110 15
- [ ] Retried POSTs (payments, orders) support an idempotency key with fingerprint check and atomic reservation, or become PUT; the key is labelled an Internet-Draft. [H §4](../reference/http-rest.md#4-idempotency-of-unsafe-requests)
- [ ] Mutable resources send strong ETags; PUT/PATCH/DELETE require `If-Match` (428 missing, 412 mismatch). [H §5](../reference/http-rest.md#5-conditional-requests-and-optimistic-concurrency-rfc-9110-sections-88-13), RFC 9110 8.8, 13
- [ ] `Cache-Control` explicit: `no-store` for secrets, never `public` on authenticated data, `Vary` correct. [H §6](../reference/http-rest.md#6-caching-rfc-9111), RFC 9111
- [ ] Errors are `application/problem+json` with own stable `type` URIs, matching `status`, a trace id, and no internals in `detail`. [H §7](../reference/http-rest.md#7-problem-details-rfc-9457), RFC 9457
- [ ] CORS: explicit origin allow-list, no wildcard with credentials, no reflected `Origin`, `UseCors` before authentication; server-side authorization still enforced. [H §8](../reference/http-rest.md#8-cors), WHATWG Fetch
- [ ] Retries only for idempotent requests and honour `Retry-After`. [H §11](../reference/http-rest.md#11-checklist)

## 6. API design

- [ ] URIs are nouns, ids opaque, no tokens or PII in URLs; per-object authorization in every handler. [A §1, §8](../reference/api-design.md#8-mass-assignment-object-level-authorization-abuse), RFC 3986
- [ ] Requests bind to input DTOs (no entities, no client-supplied `tenantId`/`role`); validation returns `ValidationProblem`; body size, depth and timeouts limited. [A §2, §8](../reference/api-design.md#8-mass-assignment-object-level-authorization-abuse), RFC 8259
- [ ] One versioning scheme; default-version behaviour decided; deprecated versions emit `Deprecation`, `Sunset` and `Link`; no breaking change inside a version. [A §3](../reference/api-design.md#3-versioning-and-deprecation), RFC 8594
- [ ] Every list paginated with an enforced server max; cursors opaque and re-authorized; `Link rel="next"`. [A §4](../reference/api-design.md#4-pagination-rfc-8288), RFC 8288
- [ ] Rate limiter partitioned by client; 429 carries `Retry-After` and Problem Details; RateLimit headers labelled a draft. [A §6](../reference/api-design.md#6-rate-limiting-and-quotas)
- [ ] PATCH is atomic, format documented, paths allow-listed, `If-Match` required. [A §7](../reference/api-design.md#7-patch-and-partial-updates), RFC 6902, RFC 7396
- [ ] Fallback authorization policy requires authentication; anonymous endpoints are explicit. [A §9](../reference/api-design.md#9-aspnet-core-practices-minimal-apis-and-controllers)
- [ ] Pipeline order checked: exception handler, forwarded headers, CORS, authentication, rate limiter, authorization. [A §9](../reference/api-design.md#9-aspnet-core-practices-minimal-apis-and-controllers)
- [ ] OpenAPI document generated, committed and diffed for breaking changes; security schemes and problem responses declared. [A §2, §11](../reference/api-design.md#11-checklist)
- [ ] Error paths tested in Production mode (`UseExceptionHandler` differs from Development); headers asserted in integration tests. [A §10](../reference/api-design.md#10-do--dont)

## 7. Webhooks and events

Standard Webhooks and CloudEvents are community specifications, not RFCs; the signature primitive is HMAC (RFC 2104). SSE is the WHATWG HTML Standard.

- [ ] Receiver reads the raw bytes once, applies a size limit, and verifies the signature before deserializing. [W §4.1](../reference/webhooks-events.md#41-read-the-raw-body), Standard Webhooks
- [ ] `webhook-id`, `webhook-timestamp` (window in both directions) and `webhook-signature` (list of `v1,` entries) parsed strictly. [W §3](../reference/webhooks-events.md#3-headers-and-the-signature-scheme), Standard Webhooks
- [ ] HMAC-SHA256 over `id.timestamp.body` with the base64-decoded `whsec_` key, compared with `FixedTimeEquals`; current and previous secret accepted during rotation. [W §4.2](../reference/webhooks-events.md#42-the-verifier-probe-tested), RFC 2104
- [ ] Idempotency store shared across instances, retention longer than the sender's retry span; 2xx returned fast, work queued, one generic 401 for failures. [W §4.3](../reference/webhooks-events.md#43-idempotent-processing), Standard Webhooks
- [ ] Sender: unique `webhook-id` per message, new timestamp per attempt, per-endpoint random secret with rotation. [W §5.2](../reference/webhooks-events.md#52-signing), Standard Webhooks
- [ ] Sender: retries with jitter from a durable queue; only 2xx is success; timeout set; delivery log, replay and auto-disable exist. [W §5.1](../reference/webhooks-events.md#51-delivery-semantics-standard-webhooks-operational-considerations), Standard Webhooks
- [ ] Sender: customer-supplied URLs are guarded against SSRF (`ConnectCallback` IP filter, IPv4-mapped IPv6 unmapped, no redirects). [W §5.3](../reference/webhooks-events.md#53-ssrf-the-customer-supplies-the-url), Standard Webhooks
- [ ] CloudEvents: `id`, `source`, `specversion`, `type` present; content mode chosen and documented. [W §6.1, §6.2](../reference/webhooks-events.md#62-http-binding-content-modes), CloudEvents 1.0.2
- [ ] CloudEvents web hook delivery: HTTPS, POST, no 3xx, `Retry-After` on 429, OPTIONS handshake if abuse protection is offered. [W §6.3](../reference/webhooks-events.md#63-http-11-web-hooks-for-event-delivery), CloudEvents HTTP 1.1 Web Hooks
- [ ] SSE: `text/event-stream`, event `id` with `Last-Event-ID` replay, keep-alives, browser-compatible auth, cancellation honoured. [W §7](../reference/webhooks-events.md#7-server-sent-events), WHATWG HTML Standard

## 8. MCP servers and AI agents

- [ ] Spec era (`2025-11-25` or `2026-07-28`) chosen and documented; `SessionMode` explicit; stateless preferred. [M §1](../reference/mcp-ai.md#1-spec-revisions), MCP spec
- [ ] Protected Resource Metadata served; its `resource` equals the audience and public URL. [M §3](../reference/mcp-ai.md#3-authorization-model), RFC 9728
- [ ] 401 carries `resource_metadata` (and `scope`); a bad token adds `invalid_token`; 403 carries `insufficient_scope`. [M §3](../reference/mcp-ai.md#3-authorization-model), RFC 6750, RFC 9728
- [ ] JwtBearer pins issuer, audience, algorithms and `ValidTypes`; no tokens in query strings. [M §4](../reference/mcp-ai.md#4-aspnet-core-wiring), RFC 8707, RFC 9068
- [ ] Per-tool authorization via `AddAuthorizationFilters()` and policies; ownership checks; `tools/list` hides forbidden tools. [M §5](../reference/mcp-ai.md#5-per-tool-authorization-and-scopes), MCP spec
- [ ] No token passthrough: downstream calls use a new token (client credentials or exchange); proxy servers have per-client consent. [M §6](../reference/mcp-ai.md#6-token-passthrough-and-confused-deputy), RFC 8693
- [ ] `Origin` validated; local HTTP on loopback and authenticated; stdio stdout reserved for the protocol. [M §2](../reference/mcp-ai.md#2-transports), MCP spec
- [ ] Tool descriptions static, outputs structured and capped, inputs validated beyond type, no shell with model input; URL-fetching tools SSRF-guarded. [M §7, §8](../reference/mcp-ai.md#8-input-validation), MCP Security Best Practices
- [ ] No secrets in source, logs or tool output; rate limiter per subject; audit logs tool, subject, outcome and denials. [M §9, §10](../reference/mcp-ai.md#10-rate-limits-and-audit), MCP Security Best Practices
- [ ] Destructive tools need single-use, server-verified approval; annotations honest. [M §11](../reference/mcp-ai.md#11-human-approval), MCP spec
- [ ] Agent side: token per server with `resource`; refresh tokens encrypted and revocable; discovery URLs SSRF-guarded. [M §12](../reference/mcp-ai.md#12-agent-token-handling), RFC 8707, RFC 7009
- [ ] Anything from an Internet-Draft (OAuth 2.1, Client ID Metadata Documents) is labelled a draft; SDK version and spec revision recorded. [M §3, §15](../reference/mcp-ai.md#15-checklist), MCP spec

## 9. Transport / TLS

- [ ] TLS 1.2 and 1.3 accepted, 1.1 and below refused; tested. [X §1](../reference/transport-tls.md#1-version-and-algorithm-policy), RFC 9325, RFC 8996
- [ ] No `DangerousAcceptAnyServerCertificateValidator` and no always-true validation callback. [X §4.1](../reference/transport-tls.md#41-default-validation-is-correct-keep-it), RFC 9525
- [ ] Certificates have SANs; renewal automated; expiry alerts; no private keys in source control. [X §7](../reference/transport-tls.md#7-operational-rules), RFC 9525
- [ ] TLS termination point documented; `ForwardedHeaders` limited to known proxies; `Request.Scheme` is `https`. [X §2](../reference/transport-tls.md#2-kestrel-server-configuration)
- [ ] HSTS on HTTPS responses with a staged `max-age`; no plain-HTTP API listener in production. [X §3](../reference/transport-tls.md#3-hsts-rfc-6797), RFC 6797
- [ ] 0-RTT disabled, or endpoints idempotent and RFC 8470 followed. [X §1](../reference/transport-tls.md#1-version-and-algorithm-policy), RFC 8446, RFC 8470
- [ ] `HttpClient` from a factory with `PooledConnectionLifetime`; custom trust uses a custom store or pinned CA, not blanket acceptance. [X §4.2](../reference/transport-tls.md#42-setting-protocols-and-trust-explicitly), RFC 9525
- [ ] mTLS: `RequireCertificate` only where all callers have certificates; chain, EKU and identity validated; forwarded certificate headers trusted only from the proxy. [X §5](../reference/transport-tls.md#5-mutual-tls), RFC 8705
- [ ] Certificate-bound tokens: `cnf.x5t#S256` compared with the presented certificate; mismatch gives `401 invalid_token`. [X §6](../reference/transport-tls.md#6-certificate-bound-access-tokens-rfc-8705), RFC 8705
- [ ] Tokens never in URLs; internal hops protected. [X §7](../reference/transport-tls.md#7-operational-rules), RFC 6750

## 10. Coding standards

Sources are Microsoft documentation pages named in [C] (C# identifier conventions, .NET coding conventions, Framework Design Guidelines, architectural principles, DI, exceptions, TAP, logging). Open/Closed, Liskov, Interface segregation, KISS, YAGNI and composition over inheritance are not defined on those pages; [C] says so.

- [ ] Names follow the casing table: PascalCase types and members, camelCase parameters, `_field`, `s_field`, `I` prefix, PascalCase constants, `Async` suffix on awaitable methods; an `.editorconfig` with `IDE1006` enforces it in the build. [C §1.1, §1.2, §1.6](../reference/coding-standards.md#11-casing-and-prefixes), Identifiers article, Framework Design Guidelines
- [ ] File-scoped namespaces, `using` directives outside the namespace, namespace shape Company.Product.Feature. [C §1.3](../reference/coding-standards.md#13-files-and-namespaces), .NET coding conventions
- [ ] Tests are named method, scenario, expected result; no magic strings; private logic tested through public members. [C §1.4](../reference/coding-standards.md#14-tests), Unit testing best practices
- [ ] JSON member naming is set once (default options write the C# name, `JsonSerializerOptions.Web` writes camelCase); URIs use one canonical case. [C §1.5](../reference/coding-standards.md#15-rest-and-json-naming), RFC 8259, RFC 3986
- [ ] State is private, invariants are enforced in methods, collections are exposed read-only; no public mutable fields or global mutable state. [C §2.1](../reference/coding-standards.md#21-encapsulation), Architectural principles, Property Design
- [ ] Each interface has a reason (second implementation, test double, boundary); abstract classes have protected constructors; no members added to shipped interfaces. [C §2.2](../reference/coding-standards.md#22-abstraction), Interface Design, Abstract Class Design
- [ ] Reuse is by composition; inheritance only for a real is-a; virtual and protected members are justified; sealing policy is consistent. [C §2.3, §2.4](../reference/coding-standards.md#23-inheritance-versus-composition), Virtual Members, Sealing
- [ ] Single responsibility: one reason to change per class; many constructor dependencies treated as a smell. [C §3.1](../reference/coding-standards.md#31-s-single-responsibility), Architectural principles, DI guidelines
- [ ] Open/Closed: variation that really grows is added as a new type, not by editing a switch. [C §3.2](../reference/coding-standards.md#32-o-openclosed), Martin (not a Microsoft page)
- [ ] Interface segregation: role-sized interfaces; no implementation throws `NotSupportedException` for an interface member. [C §3.3](../reference/coding-standards.md#33-i-interface-segregation), Martin, Framework Design Guidelines (Abstractions)
- [ ] Liskov: derived types keep the base contract; no mutable base shared by Square-like specializations. [C §3.4](../reference/coding-standards.md#34-l-liskov-substitution), Martin (not a Microsoft page)
- [ ] Dependency inversion: collaborators (clock, repositories, HTTP clients) injected through the constructor; no `new` of dependencies, no static access, no service locator. [C §3.5, §4.4](../reference/coding-standards.md#35-d-dependency-inversion), Architectural principles, DI guidelines
- [ ] No duplicated rules, no premature abstraction; duplication preferred to the wrong abstraction. [C §4.1](../reference/coding-standards.md#41-dry-kiss-yagni), Architectural principles
- [ ] DTOs and messages are records or immutable; structs only for small immutable single values. [C §4.2](../reference/coding-standards.md#42-immutability-records-and-structs), Records, Choosing Between Class and Struct
- [ ] Nullable reference types enabled; no `!` to silence warnings; public arguments validated. [C §4.3](../reference/coding-standards.md#43-nullable-reference-types), Nullable reference types
- [ ] DI lifetimes correct (no scoped service in a singleton), `ValidateScopes` and `ValidateOnBuild` on outside Development too, disposables not transient-registered. [C §4.4](../reference/coding-standards.md#44-dependency-injection), Service lifetimes, DI guidelines
- [ ] Specific exception types; no `catch (Exception)` below the top level; `throw;` not `throw ex;`; argument checks before async work; problem details at the API edge. [C §4.5](../reference/coding-standards.md#45-exceptions), Exception Throwing, Best practices for exceptions
- [ ] No `async void` (except event handlers), no `.Result` or `.Wait()`; `CancellationToken` accepted last and passed on. [C §4.6](../reference/coding-standards.md#46-asyncawait), Task-based asynchronous pattern, Asynchronous programming scenarios
- [ ] Logging uses message templates with named placeholders (no `$""`), no secrets or personal data, `[LoggerMessage]` on hot paths. [C §4.7](../reference/coding-standards.md#47-logging), Logging in .NET

## 11. Security hardening and production operations

Sources: OWASP API Security Top 10 2023 and Microsoft documentation named in [S] and [P]; items marked "author" are the guide author's recommendations, not standards. Most code in those guides is covered by the `examples/ProductionSamples` selftest; the few blocks without a sample or test (audit hash chain, `BindConfiguration` options, Kestrel limits, fallback policy, Key Vault, CI job) are still labelled unverified there.

- [ ] Every id-taking endpoint checks the caller's right to the object; every endpoint has a policy or explicit `AllowAnonymous` (fallback policy set). [S §1, §1.2](../reference/security-hardening.md#1-owasp-api-security-top-10-2023-mapped-to-aspnet-core), OWASP API1, API5
- [ ] Request, header, upload and page sizes and timeouts are bounded. [S §1.1](../reference/security-hardening.md#11-api4-set-limits-explicitly), OWASP API4
- [ ] No secrets in source, `appsettings*.json`, images or logs; user secrets in development, vault in production; required secrets validated at start. [S §2](../reference/security-hardening.md#2-secrets-management), Microsoft app-secrets article
- [ ] Server-side allow-list validation, parameterised queries, no user-built paths, URLs, JSON or HTML. [S §3](../reference/security-hardening.md#3-input-validation-and-output-encoding), OWASP API3, API10
- [ ] Security headers set (`nosniff`, CSP, `Referrer-Policy`, HSTS, `Cache-Control` for sensitive data); `Server` header off; CORS allow-list and CSRF protection when cookies are used. [S §4, §5](../reference/security-hardening.md#5-security-headers), author
- [ ] CI fails on vulnerable packages and runs SAST, secret scanning, SBOM and container scan; lock files and package source mapping used. [S §6](../reference/security-hardening.md#6-dependency-and-supply-chain-scanning-in-ci), author
- [ ] No developer exception page, dev certificate or open Swagger UI in production; non-root container. [S §7](../reference/security-hardening.md#7-secure-defaults-for-the-host), OWASP API8
- [ ] Rate limiter type fits the endpoint, is partitioned (not one global bucket), strict on credential and business-flow endpoints, returns `429` with `Retry-After` when available. [P §1.1](../reference/production-operations.md#11-the-four-built-in-limiters), Microsoft rate limiting, OWASP API4, API6
- [ ] Multi-instance rate limiting decided (gateway or shared store), because counters are per instance. [P §1.2](../reference/production-operations.md#12-configuration-partitioning-per-endpoint-policies-429-and-retry-after), Microsoft rate limiting
- [ ] Audit events record actor, object, outcome, UTC time and trace id with no secrets; separate append-only sink; tamper evidence; retention set; written atomically with the change. [P §2](../reference/production-operations.md#2-audit-logging), author
- [ ] Logging uses templates (`[LoggerMessage]` on hot paths), redacts secrets and personal data, strips CR/LF from user text, has no Trace/Debug in production, carries trace id. [P §3](../reference/production-operations.md#3-production-logging), Logging in .NET
- [ ] OpenTelemetry traces and metrics with a service name; no PII in attributes or labels; separate liveness and readiness probes not exposed with detail. [P §4](../reference/production-operations.md#4-observability), Microsoft health checks, OpenTelemetry overview
- [ ] Outbound calls use timeouts, bounded jittered retries and a circuit breaker; retries off for unsafe methods unless idempotent; background work idempotent and drains within the shutdown timeout. [P §5](../reference/production-operations.md#5-scalability-and-resilience), Microsoft HTTP resilience, generic host
- [ ] Options typed and validated on start; feature flags have owner, expiry and safe default; one versioning scheme with deprecation signalled. [P §6](../reference/production-operations.md#6-extensibility), Options pattern, RFC 9745, RFC 8594

## How to report

For each finding give: file and line, the item above, the RFC section, and the fix. Do not claim an item passes without reading the code or running a test for it.
