# API design guidelines for C# / ASP.NET Core

Scope: resource and URI design (RFC 3986), JSON (RFC 8259) and contracts (OpenAPI, JSON Schema 2020-12), versioning and deprecation, pagination (RFC 8288), error handling, rate limiting, PATCH (RFC 5789, 6902, 7396), mass assignment, and ASP.NET Core minimal API / controller practices. HTTP semantics, status codes, ETags, caching, Problem Details and CORS are in `http-rest.md`; bearer-token challenges in `oauth-oidc.md`; MCP-specific rules in `mcp-ai.md`.

**Sources.** Read from downloaded text (`_sources/api/`, `_sources/http/`; data only): RFC 5789, 6902, 7396, 8594, 9745, 8288 (header form), 3986; `draft-ietf-httpapi-ratelimit-headers-12` (Internet-Draft, expires 2027-05-24; not an RFC). OpenAPI releases checked on GitHub (latest 3.2.1, published 2026-09-10). The OpenAPI and JSON Schema documents themselves were not read in full; statements about them are limited to what the tooling output showed. **(verified)** = observed on throwaway ASP.NET Core 10 servers (`Asp.Versioning.Http` 10.2.3, `Microsoft.AspNetCore.OpenApi` 10.0.15, `JwtBearer` 10.0.15), run with curl and stopped.

---

## 1. Resources and URIs (RFC 3986)

1. URIs name **resources** (nouns), not RPC verbs: `/orders/7/items`, not `/getOrderItems`. Plural collections, stable ids. Actions that do not fit CRUD get a sub-resource or an action endpoint (`POST /orders/7/cancellation`).
2. Path = identity and hierarchy; query = filtering, sorting, paging. Do not hide the resource id in the query for single-resource operations.
3. **No secrets, tokens, PII or session ids in URIs.** URLs land in access logs, proxies, browser history and `Referer`. Bearer tokens go in `Authorization` (RFC 6750).
4. Identifiers: opaque GUIDs or random ids for externally visible ids; sequential ints let clients enumerate (and make authorization bugs exploitable). Always authorize **per object**, not only per endpoint (BOLA/IDOR).
5. Be strict about syntax: percent-encoding is case-insensitive in hex digits but compare after normalisation; reject encoded slashes and dot-segments you do not expect; do not decode twice. Use `Uri.TryCreate(..., UriKind.Absolute, ...)` and check `Scheme` for any URL a client supplies (webhook targets, redirect URIs): see `webhooks-events.md` for SSRF handling.
6. Fragments are never sent to the server; do not use them in API design. Trailing-slash and case rules: choose one canonical form, redirect or 404 the other.
7. Maximum URL length: Kestrel limits the request line and headers (`MaxRequestLineSize`, `MaxRequestHeadersTotalSize`); long filters belong in a `POST /…/search` body, not a 10 KB query string.

---

## 2. JSON and contracts

1. JSON (RFC 8259) is UTF-8. Define DTOs separate from persistence entities; never bind a request directly to an EF entity (section 8).
2. **Unknown members.** Be a tolerant reader on **responses** you consume, and a strict reader on **requests** you accept when extra fields could be mass-assignment: `JsonUnmappedMemberHandling.Disallow` makes unknown members an error. **Gotcha (verified):** with `UseExceptionHandler()` in the Development environment the resulting `BadHttpRequestException` surfaced as `500`, while in Production (`ASPNETCORE_ENVIRONMENT=Production`) the same request returned `400`. Test error paths in Production mode, or set `RouteHandlerOptions.ThrowOnBadRequest = false`.
3. **Validate** with built-in minimal API validation (.NET 10: `builder.Services.AddValidation()` with `System.ComponentModel.DataAnnotations` attributes on records): an invalid body returned `400` with `errors: { "Name": [...], "Qty": [...] }` as a `ValidationProblem` (verified). Add semantic validation (cross-field, existence, ownership) in the handler or a filter.
4. **Document the contract.** Generate an OpenAPI document (`builder.Services.AddOpenApi(); app.MapOpenApi();` from `Microsoft.AspNetCore.OpenApi`; output verified as `"openapi": "3.1.1"`). OpenAPI 3.1 uses JSON Schema 2020-12 as its schema dialect (JSON Schema's own `$schema` identifies the dialect). The current OpenAPI release on GitHub is 3.2.1; check what your generator and client tooling support before choosing.
5. Treat the OpenAPI file as a **reviewed artifact**: commit it, diff it in pull requests, run a breaking-change check in CI, declare `securitySchemes` (OAuth2 flows, bearer JWT), list `responses` for 400/401/403/404/409/422/429 with `application/problem+json`. Serve it publicly only if the API surface is public; the document reveals every route.
6. Numbers: use string for 64-bit ids and money in JSON consumed by JavaScript (IEEE 754 doubles lose integers above 2^53); use ISO 8601 UTC timestamps (`DateTimeOffset`); define enums as strings.
7. Set request limits: `Kestrel Limits.MaxRequestBodySize` (an oversized body returned `413` as Problem Details, verified), `JsonSerializerOptions.MaxDepth`, collection size caps in validators, and request timeouts (`AddRequestTimeouts`/`UseRequestTimeouts`).

---

## 3. Versioning and deprecation

**Rules**
1. Version from day one. Choose **one** scheme and document it: URL segment (`/v1/…`, simplest, cacheable, visible in logs), header (`api-version`, `Accept` media-type parameter), or query. Do not mix.
2. A **breaking change** (new major) is: removing or renaming a field, endpoint or enum value; changing a type, format, units or nullability; making an optional input required; tightening validation; changing error codes or semantics; changing auth scopes. **Additive** changes (new optional request field, new response field, new endpoint, new enum value only if clients were told to tolerate unknown values) stay in the same version. Say in your docs that clients must ignore unknown response fields and unknown enum values.
3. Use `Asp.Versioning.Http` 10.2.3 (minimal APIs) or `Asp.Versioning.Mvc` (controllers). Do **not** assume a default version silently: set `AssumeDefaultVersionWhenUnspecified = false` so clients pin one; `ReportApiVersions = true` adds `api-supported-versions` and `api-deprecated-versions` response headers (verified: `api-supported-versions: 1.0`, `api-deprecated-versions: 0.9`). Unknown versions return `404` Problem Details in this setup (verified); decide whether you prefer `400 UnsupportedApiVersion` and test it.
4. **Deprecate with standard headers.** RFC 9745 `Deprecation: @1688169599` (a Structured Field **Date**, seconds since the epoch, not an HTTP-date) says the resource is or will be deprecated; RFC 8594 `Sunset: Sat, 31 Dec 2026 23:59:59 GMT` (HTTP-date) says when it is expected to stop working. Both are hints that clients cannot depend on. Add `Link: <https://api.example.com/docs/migrate-v2>; rel="deprecation"` and `rel="successor-version"` pointing at documentation or the new resource, and also announce by email and in the changelog. Keep a minimum overlap period (at least 6 to 12 months for external APIs) and watch usage per client before removal.
5. Run old and new versions from shared domain code with version-specific DTOs and mappers; do not fork the whole service.
6. Never version by renaming a field without keeping the old one during the overlap.

```csharp
builder.Services.AddApiVersioning(o =>
{
    o.DefaultApiVersion = new ApiVersion(1, 0);
    o.AssumeDefaultVersionWhenUnspecified = false;     // explicit client choice
    o.ReportApiVersions = true;
    o.ApiVersionReader = new UrlSegmentApiVersionReader();
});

var versions = app.NewApiVersionSet()
    .HasApiVersion(new ApiVersion(1, 0))
    .HasDeprecatedApiVersion(new ApiVersion(0, 9))
    .ReportApiVersions().Build();
var v1 = app.MapGroup("/v{version:apiVersion}").WithApiVersionSet(versions).MapToApiVersion(1, 0);

// emit RFC 9745 / 8594 headers for a deprecated route (in a filter or middleware):
ctx.Response.Headers["Deprecation"] = "@1688169599";
ctx.Response.Headers["Sunset"] = "Sat, 31 Dec 2026 23:59:59 GMT";
ctx.Response.Headers.Append("Link", "<https://api.example.com/docs/migrate-v2>; rel=\"deprecation\"");
```

Note: the OpenAPI document generated for the versioned group showed the path as `/v{version}/items` (verified); to get `/v1/items` per version you need the versioning API Explorer package (`Asp.Versioning.Mvc.ApiExplorer` or the OpenAPI integration) and one document per version.

---

## 4. Pagination (RFC 8288)

1. **Every list endpoint paginates**, with a server-enforced maximum page size (`Math.Clamp(limit ?? 10, 1, 20)`). Unbounded lists are a denial-of-service and data-exposure risk.
2. Prefer **cursor (keyset) pagination** over `offset/limit`: stable under inserts and deletes, constant cost, no deep-offset scans. Needs a deterministic total order (`ORDER BY created_at, id`); the cursor encodes the last key. Offset paging is acceptable for small, slowly changing sets.
3. Make cursors **opaque** to clients (base64url of a signed or encrypted payload via `IDataProtector`) when they encode filters or sort keys; a client must not be able to alter them to read other tenants' data. Authorization and tenant filters are re-applied on every page, never taken from the cursor.
4. Navigate with **`Link` headers** (RFC 8288) using registered relation types `next`, `prev`, `first`, `last`, and/or a body field (`nextCursor`), and tell clients to follow links rather than build URLs. `rel` values are case-insensitive tokens; target URIs are in `<>`.
5. Fetch `size + 1` rows to know whether another page exists without a `COUNT(*)`; give totals only when cheap and needed.
6. Return the page object, not a bare array, so you can add fields later without a breaking change.

Verified output for 25 items, `limit` clamped to 20:

```csharp
v1.MapGet("/items", (HttpContext ctx, int? after, int? limit) =>
{
    var size = Math.Clamp(limit ?? 10, 1, 20);                            // server-enforced bounds
    var page = all.Where(i => i.Id > (after ?? 0)).Take(size + 1).ToList();
    var hasMore = page.Count > size;
    if (hasMore) page.RemoveAt(page.Count - 1);
    if (hasMore)
        ctx.Response.Headers.Link =
            $"<{ctx.Request.Scheme}://{ctx.Request.Host}/v1/items?after={page[^1].Id}&limit={size}>; rel=\"next\"";
    return Results.Ok(new { items = page, nextCursor = hasMore ? page[^1].Id.ToString() : null });
});
// page 1: Link: <http://localhost:5197/v1/items?after=10&limit=10>; rel="next"
// ?after=20&limit=50: five items, "nextCursor": null, no Link header
```

(In production derive the public base URL from configuration, not from request `Host`, to avoid host-header poisoning.)

---

## 5. Errors

Use `application/problem+json` (RFC 9457) for every error with a stable `type`, a `traceId`, and `errors` for validation; status code rules, `detail` hygiene and registration are in `http-rest.md` section 7. API-level rules:
1. One error shape across all endpoints, controllers and middleware (`AddProblemDetails`, `UseExceptionHandler`, `UseStatusCodePages`, an `IExceptionHandler` mapping domain exceptions).
2. Publish an **error catalogue** (type URI, status, meaning, remedy) in the OpenAPI document or docs; clients switch on `type`, never on `detail` or `title`.
3. Do not distinguish "does not exist" from "not yours" when that leaks existence: return `404`.
4. Validation errors name fields with the same casing as the JSON contract; never echo secrets back.
5. Log the full exception server-side with the `traceId`; return only the id to the client.

---

## 6. Rate limiting and quotas

1. Put rate limiting in the app (`Microsoft.AspNetCore.RateLimiting`, `AddRateLimiter` + `UseRateLimiter()` after authentication) and again at the gateway. Partition by authenticated client or tenant (`client_id` claim), not only by IP; unauthenticated endpoints by IP with stricter limits. Use separate policies for expensive endpoints, for auth endpoints (credential stuffing), and for writes.
2. Rejected requests return **`429 Too Many Requests`** with `Retry-After` (RFC 6585, RFC 9110 10.2.3) and a Problem Details body. The framework does not add `Retry-After` for you: read the lease metadata in `OnRejected`. Verified: after three requests per 30 s window the fourth returned `429`, `Retry-After: 30`, `application/problem+json`.
3. Optional quota headers follow `draft-ietf-httpapi-ratelimit-headers-12` (Internet-Draft; the field names changed between drafts, so label it a draft and do not hard-code it for clients). Draft 12 examples: `RateLimit-Policy: burst;q=100;w=60,"daily";q=1000;w=86400` (policy: quota `q` per window `w` seconds) and `RateLimit: "default";a=50;w=30` (`a` = quota still available, `w` = effective window; optional `pk` partition key and `c` cost). It also defines the problem type `https://iana.org/assignments/http-problem-types#quota-exceeded` with a `violated-policies` array, which the sample's `type` uses. Clients must not assume the headers exist, and must ignore malformed values; a response may carry both `RateLimit` and `Retry-After`.
4. Clients: honour `Retry-After`, add jitter and exponential backoff, and retry only idempotent requests (`http-rest.md` section 4).
5. Security considerations from the draft: quota headers can disclose information about other tenants and your capacity; throttling does not stop a client from sending requests (still protect the origin); do not expose per-IP counters to unauthenticated callers.
6. Behind a proxy, configure `ForwardedHeaders` so the limiter sees the real client IP; otherwise everyone shares one bucket.

```csharp
builder.Services.AddRateLimiter(o =>
{
    o.OnRejected = async (ctx, ct) =>
    {
        ctx.HttpContext.Response.StatusCode = 429;
        if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
            ctx.HttpContext.Response.Headers.RetryAfter = ((int)retry.TotalSeconds).ToString();
        await Results.Problem(statusCode: 429, title: "Too many requests",
            type: "https://iana.org/assignments/http-problem-types#quota-exceeded")
            .ExecuteAsync(ctx.HttpContext);
    };
    o.AddPolicy("api", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.User.FindFirst("client_id")?.Value ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 100, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
group.RequireRateLimiting("api");
```

---

## 7. PATCH and partial updates

1. PATCH (RFC 5789) is **neither safe nor idempotent**, and the server MUST apply the whole patch **atomically** (all changes or none). Protect it with `If-Match` (`http-rest.md` section 5).
2. Choose a patch format and document its media type:
   - **JSON Merge Patch** (RFC 7396, `application/merge-patch+json`): send a partial document; `null` deletes a member; arrays are replaced wholesale; cannot set a value to `null`. Simple, good for flat DTOs.
   - **JSON Patch** (RFC 6902, `application/json-patch+json`): an array of operations `add`, `remove`, `replace`, `move`, `copy`, `test` with JSON Pointer (RFC 6901) paths. Precise for arrays; `test` enables optimistic checks inside the patch.
3. **Allow-list patchable paths.** A JSON Patch with `"path": "/role"` or `/ownerId` is mass assignment by another name. Apply patches to a DTO that contains only client-editable fields, validate the patched DTO, and only then map to the entity.
4. Reject unknown ops and paths with `422` (or `400`), and cap the number of operations and document size.
5. A failed `test` operation means the whole patch fails (RFC 6902 5): return `409` or `422` with a Problem Details body.
6. For simple state changes prefer a dedicated sub-resource or PUT of one field (`PUT /orders/7/status`), which is idempotent by definition.

---

## 8. Mass assignment, object-level authorization, abuse

1. Bind requests to **input DTOs** that list exactly the writable fields; never to entities with `IsAdmin`, `TenantId`, `OwnerId`. Map explicitly (or with a mapper that fails on unmapped members).
2. Take identity (`sub`, tenant, owner) from the validated token, never from the body, query or a header the client sets.
3. Authorize **each object access** (`order.OwnerId == user.Sub` or a resource-based `IAuthorizationService.AuthorizeAsync(user, order, "CanEdit")`), including list endpoints (filter in the query) and bulk operations.
4. Apply **secure by default**: set `FallbackPolicy` to require an authenticated user and opt out with `AllowAnonymous()`. Verified: an endpoint with no authorization metadata returned `401` with `WWW-Authenticate: Bearer`, an explicit `AllowAnonymous()` endpoint returned `200`.
5. Limit expensive input: page size, number of ids in a filter, nested expansion depth (`?expand=`), file sizes, request duration.
6. Make mutations concurrency-safe (`rowversion`, ETag/`If-Match`) and retry-safe (`Idempotency-Key`, `http-rest.md`).

---

## 9. ASP.NET Core practices: minimal APIs and controllers

| Concern | Minimal APIs | Controllers |
|---|---|---|
| Grouping | `MapGroup("/v1/orders")` with `RequireAuthorization`, `WithTags`, filters | `[ApiController]`, `[Route]`, base controller |
| Validation | `AddValidation()`, endpoint filters | `[ApiController]` automatic 400 `ValidationProblem` |
| Results | `TypedResults`, `Results<Ok<T>, NotFound, ProblemHttpResult>` | `ActionResult<T>`, `[ProducesResponseType]` |
| Cross-cutting | `IEndpointFilter`, middleware | Filters, middleware |
| OpenAPI | `WithName`, `Produces<T>`, `Accepts<T>` metadata | attributes, `Microsoft.AspNetCore.OpenApi` |

**Rules**
1. Pick one style per service. Minimal APIs with `MapGroup` and `TypedResults` give compile-time response contracts; controllers with `[ApiController]` suit large teams with conventions. Keep handlers thin: parse, authorize, call an application service, map the result.
2. Order the pipeline deliberately: `UseExceptionHandler` first, then forwarded headers, HTTPS/HSTS, `UseCors` (before auth), `UseAuthentication`, `UseRateLimiter`, `UseAuthorization`, endpoints. Wrong order is a common source of 401s on preflights and unthrottled auth.
3. Async all the way; flow `CancellationToken` into data access (`HttpContext.RequestAborted` is bound automatically to a `CancellationToken` parameter).
4. Use `IHttpClientFactory` with typed clients and resilience handlers (`Microsoft.Extensions.Http.Resilience`) for outbound calls: timeouts, bounded retries for idempotent calls only, circuit breaker.
5. Configuration validated at startup (`ValidateOnStart`), secrets from user-secrets or a vault.
6. Health checks (`/healthz`) are unauthenticated but must not reveal versions or dependencies in detail; keep metrics endpoints off the public listener.
7. Structured logging with `traceId` correlation; redact `Authorization`, cookies, tokens and PII. Emit `Activity` spans (OpenTelemetry) per request.
8. Endpoint-level tests: `WebApplicationFactory` for status codes, headers (`WWW-Authenticate`, `Allow`, `Location`, `ETag`, `Retry-After`) and Problem Details shapes; run them in **Production** environment mode as well.

---

## 10. Do / Don't

**Do**
- Version explicitly, deprecate with `Deprecation`/`Sunset` headers and a documented overlap.
- Paginate every list with a server-side max; prefer cursors; return `Link` and `nextCursor`.
- Return Problem Details with stable `type` URIs and a `traceId` everywhere.
- Rate limit per client with `429` + `Retry-After`; label RateLimit headers as a draft.
- Bind to input DTOs, validate, authorize per object, set a fallback authorization policy.
- Treat PATCH as atomic; allow-list patch paths; require `If-Match`.
- Commit and diff the OpenAPI document; run breaking-change checks.

**Don't**
- Put tokens or PII in URLs; expose sequential ids without per-object authorization.
- Return unbounded lists or accept unbounded bodies.
- Bind JSON straight to entities or accept `tenantId`/`role` from the client.
- Change types, units or semantics inside a version.
- Rely on default API versions silently, or on `Deprecation`/`Sunset` as the only notice.
- Reveal stack traces, SQL or internal hostnames in errors.
- Assume `UseExceptionHandler` behaves identically in Development and Production.

---

## 11. Checklist

- [ ] URIs are nouns; ids are opaque; no secrets in URLs; per-object authorization on every handler.
- [ ] Requests bind to DTOs; unknown members and over-posting handled; validation returns `ValidationProblem`; limits set (body, depth, timeouts).
- [ ] OpenAPI generated, committed, reviewed; security schemes and `application/problem+json` responses declared.
- [ ] One versioning scheme; `AssumeDefaultVersionWhenUnspecified` decision recorded; deprecated versions emit `Deprecation`, `Sunset`, `Link`.
- [ ] All lists paginated with enforced max; cursors opaque and re-authorized; `Link rel="next"` present.
- [ ] Rate limiter partitioned by client; `429` includes `Retry-After` and Problem Details; draft headers labelled.
- [ ] PATCH atomic, format documented, paths allow-listed, `If-Match` required.
- [ ] Fallback authorization policy requires authentication; anonymous endpoints are explicit.
- [ ] Pipeline order verified (exception handler, forwarded headers, CORS, authentication, rate limiter, authorization).
- [ ] Error paths tested in Production environment mode; headers asserted in integration tests.
