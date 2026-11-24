---
name: dotnet-auth-standards
description: Use when writing, reviewing or designing C# / ASP.NET Core code that touches OAuth 2.0/2.1, OpenID Connect, JWT or bearer tokens, API authentication and authorization, PKCE, DPoP, token exchange, client credentials, device flow, protected resource or authorization server metadata, REST/HTTP semantics, Problem Details, webhooks, SSE, MCP servers or clients, or TLS/mTLS, API security hardening (OWASP API Top 10, secrets, security headers), rate limiting, audit and production logging, observability and resilience, and for general C# coding standards (naming conventions, OOP, SOLID, DI, exceptions, async, logging). Apply it even when the user does not name an RFC, for example "add login", "secure this API", "validate this token", "review my auth code".
---

# .NET auth and API standards

Guidelines that make C# / ASP.NET Core code follow the real standards (RFCs and specs) instead of habits. Each area has a reference file with rules, one tested example and a review checklist. Load only the file for the area you are working on.

## Status

| Area | File | State |
|---|---|---|
| OAuth 2.0/2.1 (6749, 6750, 7636, 9700, 8693, 7523, 8707, 8414, 9728, 7591, 7662, 7009, 8628, 9449) | [reference/oauth-oidc.md](reference/oauth-oidc.md) | written |
| Tokens/identity: JWT 7519, JWS 7515, JWK 7517, RFC 9068, OIDC Core + Discovery, SCIM 7644, JWT pitfalls (RFC 8725) | [reference/tokens-jwt.md](reference/tokens-jwt.md) | written |
| REST/HTTP: RFC 9110, 9111, 9457, Idempotency-Key draft, CORS | [reference/http-rest.md](reference/http-rest.md) | written |
| API design: OpenAPI, JSON Schema, RFC 9457, 8259, 6902, 7396, 8288, 3986, 9745, 8594 | [reference/api-design.md](reference/api-design.md) | written |
| Webhooks/events: Standard Webhooks, HMAC, CloudEvents, SSE | [reference/webhooks-events.md](reference/webhooks-events.md) | written |
| MCP, JSON-RPC 2.0 (Agent2Agent not covered) | [reference/mcp-ai.md](reference/mcp-ai.md) | written |
| TLS 1.3, mTLS | [reference/transport-tls.md](reference/transport-tls.md) | written |
| General .NET coding standards: naming, OOP, SOLID, DRY/KISS/YAGNI, records, nullable, DI, exceptions, async, logging | [reference/coding-standards.md](reference/coding-standards.md) | written |
| Security hardening: OWASP API Security Top 10 (2023) mapping, secrets, input validation, security headers, supply-chain scanning in CI, secure defaults | [reference/security-hardening.md](reference/security-hardening.md) | written (headers, validation verified by `examples/ProductionSamples` selftest; Kestrel limits, fallback policy, Key Vault line, CI job still unverified) |
| Production operations: rate limiting (4 limiters), audit logging, production logging, OpenTelemetry and health probes, resilience, options/feature flags/versioning | [reference/production-operations.md](reference/production-operations.md) | written (verified by `examples/ProductionSamples` selftest except the audit hash chain and `BindConfiguration` options block, which stay unverified) |
| Combined review checklist (all areas included: OAuth/OIDC, tokens, identity, REST, API design, webhooks, MCP, TLS, coding standards, security hardening, production operations) | [checklists/code-review.md](checklists/code-review.md) | written |

If the needed file is "not yet written", say so and answer from the RFC itself rather than from memory; do not pretend the skill covered it.

## When to use which file

- Sign-in, access tokens, scopes, refresh, client registration, discovery, token exchange, device flow, DPoP: `reference/oauth-oidc.md`.
- Validating or issuing JWTs, JWKS and key rotation, `alg none` / algorithm confusion / issuer / audience / clock skew, `at+jwt` access tokens, ID tokens, `nonce`, UserInfo, OIDC discovery, SCIM endpoints: `reference/tokens-jwt.md` (start with its pitfalls table).
- Naming, OOP, SOLID, DRY/KISS/YAGNI, records, nullable reference types, DI, exceptions, async/await, logging in C#: `reference/coding-standards.md` (sources named per rule; says which principles Microsoft pages do not define). Compiled good/bad samples: `examples/CodingStandards/` (`dotnet run`) and `examples/CodingStandards.Bad/`.
- OWASP API Top 10, secrets, input validation, security headers, dependency/secret/SBOM/container scanning, secure defaults: `reference/security-hardening.md`. Rate limiting, audit logging, safe production logging, OpenTelemetry, health probes, resilience, graceful shutdown, options, feature flags: `reference/production-operations.md` (each says which code was run in `examples/ProductionSamples` and which few blocks remain unverified).
- Compiled, working C# for those: `examples/OAuthSamples/` (build with `dotnet build`, logic checks with `dotnet run -- selftest`).

## Core rules (apply without opening a reference file)

1. **Authorization code + PKCE (S256)** for anything with a user. Never implicit, never password grant (RFC 9700; removed in OAuth 2.1).
2. **Redirect URIs by exact string match.** No wildcards or prefixes. Only loopback IP redirects may vary the port.
3. **Bearer tokens only in the `Authorization` header.** Never in URLs or logs (RFC 6750, 9700).
4. **Validate on the API**: issuer, signature, lifetime and audience (`ValidateAudience = true`, `ValidAudience` = this API). A valid token for another audience is rejected (RFC 8707, 9700).
5. **Authorize by scope or role as well as authenticate.** `scope` is a space-delimited string; split it, never `Contains` on the raw string.
6. **Send the right challenge.** 401 plus `WWW-Authenticate: Bearer` (with `error="invalid_token"` only if a token was sent and failed); 403 for insufficient scope. Advertise `resource_metadata` (RFC 9728).
7. **Discover, do not hard-code.** Use authority metadata (RFC 8414 / OIDC discovery); the document's `issuer` must exactly equal the identifier used to find it.
8. **Secrets stay out of source.** User secrets, environment or a vault. Public clients have no secret.
9. **Sender-constrain and rotate.** Public-client refresh tokens must be rotated or DPoP-bound; revoke on logout (RFC 7009).
10. **Downstream calls get their own token** (client credentials or RFC 8693 exchange with the right `audience`/`resource`), never the inbound one.
11. **Pin the JWT validation inputs.** Explicit issuer, audience, algorithm allow-list (never `none`, never HS* with a public key), `typ` `at+jwt` for access tokens, small clock skew, keys by `kid` from the issuer's JWKS. Never follow `jku`/`x5u` from a token.
12. **State what is a draft.** OAuth 2.1 is an Internet-Draft; cite RFC 9700 for binding rules.

## How to work with this skill

1. Identify the area; open its reference file; follow its rules.
2. When writing code, adapt the matching example in `examples/` rather than writing from memory; keep option names that the sample compiles with.
3. When reviewing, walk the checklist at the end of each reference section and report findings with the RFC section.
4. Verify claims: build the code (`dotnet build`) and run the relevant check before saying it works.
5. If the target framework differs from net10.0, check that the package versions exist for it (`dotnet add package` fails with NU1202 otherwise).

## Conventions in this skill
- Examples target net10.0 with `Microsoft.AspNetCore.Authentication.JwtBearer` and `.OpenIdConnect` 10.0.15.
- No company, personal or credential data appears here; all hosts are `example` domains.
