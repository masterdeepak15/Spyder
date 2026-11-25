# Code-review checklist: REST / HTTP semantics and API design

For reviewing C# / ASP.NET Core changes. Each item points to the reference section with the rule and example, and to the RFC or specification. Mark an item N/A only if the code does not touch that area. Short rules: see the matching file in `../reference/` (routing in `../SKILL.md`).

Reference files: [H] `../reference/http-rest.md`, [A] `../reference/api-design.md`.

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

## How to report

For each finding give: file and line, the item above, the RFC section, and the fix. Do not claim an item passes without reading the code or running a test for it.
