---
name: dotnet-auth-standards
description: Use when writing, reviewing or designing C# / ASP.NET Core code that touches OAuth 2.0/2.1, OpenID Connect, JWT or bearer tokens, API authentication and authorization, PKCE, DPoP, token exchange, client credentials, device flow, protected resource or authorization server metadata, REST/HTTP semantics, Problem Details, webhooks, SSE, MCP servers or clients, TLS/mTLS, API security hardening (OWASP API Top 10, secrets, security headers), rate limiting, audit and production logging, observability, resilience, and general C# coding standards (naming, OOP, SOLID, DI, exceptions, async, logging). Apply it even when the user does not name an RFC, e.g. "add login", "secure this API", "validate this token", "review my auth code".
---

# .NET auth and API standards

Load only the files for the task's area. Writing code: the **rules** file (short), then **details** only for the section you need. Reviewing: the matching **checklist**. If a file is missing, answer from the RFC and say so.

| Task area | Rules (load first) | Details (on demand) | Review checklist |
|---|---|---|---|
| OAuth 2.0/2.1: flows, PKCE, bearer, scopes, discovery, exchange, device, DPoP | reference/oauth-oidc.md | reference/oauth-oidc-details.md | checklists/oauth-oidc.md |
| JWT/JWS/JWK validation, RFC 9068, OIDC ID token/UserInfo/discovery, SCIM | reference/tokens-jwt.md | reference/tokens-jwt-details.md | checklists/tokens-identity.md |
| REST/HTTP: methods, status, caching, ETag, Problem Details, CORS | reference/http-rest.md (full) | - | checklists/rest-api.md |
| API design: versioning, pagination, PATCH, OpenAPI | reference/api-design.md (full) | - | checklists/rest-api.md |
| Webhooks, CloudEvents, SSE | reference/webhooks-events.md (full) | - | checklists/webhooks.md |
| MCP servers/clients, JSON-RPC | reference/mcp-ai.md (full) | - | checklists/mcp.md |
| OpenAPI documents, JSON Schema 2020-12 | reference/openapi-jsonschema.md (full) | - | checklists/openapi-jsonschema.md |
| A2A agents: Agent Card, tasks, push webhooks | reference/a2a.md (full) | - | checklists/a2a.md |
| TLS, mTLS, HSTS | reference/transport-tls.md (full) | - | checklists/tls.md |
| C# naming, OOP, SOLID, DI, exceptions, async, logging | reference/coding-standards.md | reference/coding-standards-details.md | checklists/coding-standards.md |
| OWASP API Top 10, secrets, validation, headers, CI scanning | reference/security-hardening.md | reference/security-hardening-details.md | checklists/security-ops.md |
| Rate limiting, audit, logging, OpenTelemetry, health, resilience, options | reference/production-operations.md | reference/production-operations-details.md | checklists/security-ops.md |

"(full)" = not yet split; the single file is both rules and details. Checklist index: checklists/code-review.md.

Samples (compiled): `examples/OAuthSamples`, `examples/ProductionSamples`, `examples/CodingStandards` (+ `.Bad`). Adapt them, do not write from memory; run `dotnet build` and the selftest before claiming it works. Targets net10.0; on another framework check package versions (NU1202).

Always, without opening a file: authorization code + PKCE S256 only; exact redirect URI match; bearer tokens only in the `Authorization` header; validate issuer, signature, lifetime, audience and `typ`; no secrets in source; OAuth 2.1 is an Internet-Draft, cite RFC 9700.
