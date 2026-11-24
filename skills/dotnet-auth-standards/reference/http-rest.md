# HTTP and REST guidelines for C# / ASP.NET Core

Scope: HTTP semantics (RFC 9110), caching (RFC 9111), status codes, idempotency, conditional requests, Problem Details (RFC 9457), CORS, and the REST constraints from Fielding's dissertation chapter 5. API-shape topics (versioning, pagination, rate-limit headers, OpenAPI) are in `api-design.md`; webhooks in `webhooks-events.md`; TLS in `transport-tls.md`; bearer-token challenges in `oauth-oidc.md`.

**Sources.** Read from downloaded text (in `_sources/http/`, data only): RFC 9110 sections 8.8, 9.2, 9.3, 13.1, 15; RFC 9111 section 3.5; RFC 9457 sections 3 to 5; RFC 6585; `draft-ietf-httpapi-idempotency-key-header-09` (Internet-Draft, expires 2027-05-25; not an RFC); Fielding chapter 5. CORS is defined by the WHATWG Fetch Standard, which was **not** read for this file; the CORS rules below rest on browser behaviour as commonly documented plus ASP.NET Core behaviour. **(verified)** marks behaviour observed on a throwaway ASP.NET Core 10 minimal API server (run, curl, stopped).

---

## 1. REST in one paragraph

Fielding 5.1.5: the uniform interface is four constraints: identification of resources, manipulation through representations, self-descriptive messages, and hypermedia as the engine of application state. Layered system (5.1.6) means intermediaries (proxies, gateways, caches) may sit between client and server, so every request must carry what it needs and every response must say how it may be cached. Practical consequence: use the HTTP method and status code semantics exactly, make requests stateless (credentials in `Authorization` on each request, no server-side session needed to interpret them), and describe cacheability explicitly. Fielding's HATEOAS constraint is rarely followed in JSON APIs; if you ship "REST" without it, call it an HTTP API and document it with OpenAPI (`api-design.md`).

---

## 2. Methods (RFC 9110 section 9)

| Method | Safe | Idempotent | Use |
|---|---|---|---|
| GET, HEAD, OPTIONS | yes | yes | read; never change state |
| PUT | no | **yes** | create or replace the resource at this URI |
| DELETE | no | **yes** | remove the mapping to the resource |
| POST | no | **no** | create under a collection, run an action |
| PATCH (RFC 5789, not in 9110) | no | not guaranteed | partial update; design it idempotent or protect it with `If-Match` |

**Rules**
1. **GET must be safe.** No side effects a user would object to (9.2.1): do not delete, send mail or advance a counter on GET. Crawlers, prefetchers and link previews issue GETs.
2. **Idempotent means repeated identical requests have the same intended effect** (9.2.2: PUT, DELETE and safe methods). That is why a client may automatically retry them after a lost response; a client SHOULD NOT automatically retry a non-idempotent request, and a proxy MUST NOT. `HttpClient` resilience handlers (Polly, `Microsoft.Extensions.Http.Resilience`) retry what you configure: restrict retries to idempotent methods or requests carrying an idempotency key.
3. **PUT** (9.3.4): `201 Created` if it created the resource, `200 OK` or `204 No Content` if it replaced one. The server SHOULD reject a representation inconsistent with the resource (`409`, or `415` for a wrong `Content-Type`).
4. **POST creating a resource**: `201 Created` with a `Location` header identifying the new resource (15.3.2), body optional. Use `TypedResults.Created(uri, body)`.
5. **DELETE** (9.3.5): `200` with a status body, `202` if deletion is queued, or `204`. Repeating a DELETE on an already-deleted resource may return `404`; the effect is still idempotent. Do not give DELETE a request body.
6. Action endpoints that are not CRUD (`POST /orders/7:cancel`, `POST /orders/7/cancellation`) are fine as POST; do not tunnel actions through GET or hide a delete behind PUT.
7. `405 Method Not Allowed` MUST include an `Allow` header listing supported methods (15.5.6). ASP.NET Core routing returns 405 with `Allow` for known routes; verify it when you add custom middleware.
8. Do not expose `TRACE`; disable `CONNECT` and unneeded methods at the proxy.

---

## 3. Status codes that matter (RFC 9110 section 15, RFC 6585)

| Situation | Code | Notes |
|---|---|---|
| Read, update with body | 200 | |
| Created | **201** + `Location` | 15.3.2 |
| Accepted for async work | **202** | return a status URL (`Location`) and a way to poll; do not pretend it finished |
| Success, nothing to return | 204 | no body at all |
| Not modified | 304 | conditional GET; no body; keep validators |
| Malformed syntax, bad parameters | 400 | |
| No/invalid credentials | **401** | MUST send `WWW-Authenticate` (15.5.2) |
| Authenticated but not allowed | **403** | send `WWW-Authenticate: Bearer error="insufficient_scope"` for scope shortfalls (RFC 6750) |
| Unknown or hidden resource | 404 | return 404 instead of 403 when revealing existence would leak data |
| Wrong method | 405 + `Allow` | |
| Cannot produce acceptable media type | 406 | |
| Conflict with current state | **409** | duplicate key, state machine violation, in-flight idempotent request |
| Precondition failed | **412** | `If-Match` / `If-Unmodified-Since` false |
| Request too large | 413 | |
| Unsupported request `Content-Type` | **415** | |
| Well-formed but semantically invalid | **422** | JSON parses, business or validation rule fails; many APIs use 400 for both, pick one and document it |
| Precondition header required | **428** (RFC 6585) | require `If-Match` on PUT/PATCH to prevent lost updates |
| Rate limited | **429** (RFC 6585) | MAY include `Retry-After` |
| Server fault | 500 | generic body, no stack trace |
| Upstream fault | 502 / 504 | |
| Temporarily unavailable | 503 + `Retry-After` | |

**Rules**
1. **401 vs 403.** 401 means "authenticate (again)" and carries a challenge; 403 means "I know who you are and the answer is no". Never return 401 without `WWW-Authenticate`; never return 200 with an error in the body.
2. Success is `2xx` only. Do not return `200` with `{"success": false}`.
3. Match the code to the error class: client mistakes `4xx` (do not retry unchanged), server failures `5xx` (retry may help). Clients should retry `429` and `503` after `Retry-After`, not blindly on `4xx`.
4. Use `TypedResults` and `ProducesResponseType` / `Results<Ok<T>, NotFound, ProblemHttpResult>` so the status set is part of the OpenAPI contract.

---

## 4. Idempotency of unsafe requests

PUT and DELETE are idempotent by definition; POST is not, yet clients must retry it after timeouts. Two tools:

**A. Design the operation to be idempotent.** Prefer PUT with a client-chosen id (`PUT /orders/{guid}`) over server-assigned ids, upserts keyed by a natural key, and state-setting actions (`set status = shipped`) over increments.

**B. `Idempotency-Key` header.** `draft-ietf-httpapi-idempotency-key-header-09` (Internet-Draft; it may change, label it a draft) defines a request header whose value is an RFC 9651 Structured Field **String** (so it is sent quoted), unique per logical request, RECOMMENDED to be a UUID or similarly random, never reused with a different payload.
- Server stores the key (scoped to the authenticated client, see below), a **fingerprint** of the payload, and the response.
- First request: process normally. Same key and same fingerprint after completion: return the original response (a "response consistent with the request being completed"). Same key while the first is still running: `409 Conflict`. Same key, different fingerprint: `422`. Missing key on an operation that requires it: `400` (draft 2.6 to 2.7). Errors should use RFC 9457 bodies.
- Security section of the draft: keys must be validated against a published format (injection), and low-entropy keys let attackers read other clients' cached results, so use a **composite lookup key** (client or tenant id from the token plus the idempotency key). Expire keys on a published schedule.

Behaviour exercised **(verified: first POST 201, identical retry replayed 201 with the same body, changed payload 422, no key 400)** on a probe that used an in-memory `ConcurrentDictionary`; the snippet below is that logic with the store abstracted behind `IIdempotencyStore` (the abstraction itself was not compiled):

```csharp
app.MapPost("/orders", async (HttpContext ctx, IIdempotencyStore store) =>
{
    var key = ctx.Request.Headers["Idempotency-Key"].ToString().Trim('"');   // structured-field string
    if (!Guid.TryParse(key, out _))
        return Results.Problem(statusCode: 400, title: "Idempotency-Key is missing or not a UUID");

    using var sr = new StreamReader(ctx.Request.Body);
    var body = await sr.ReadToEndAsync();
    var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body)));
    var composite = $"{ctx.User.FindFirstValue("client_id")}:{key}";        // never the raw key alone

    if (store.TryGet(composite, out var seen))
        return seen.Fingerprint == fingerprint
            ? Results.Text(seen.Body, "application/json", statusCode: seen.Status)   // replay
            : Results.Problem(statusCode: 422, title: "Idempotency-Key reused with a different payload");

    // production: reserve the key atomically BEFORE doing work (return 409 to concurrent duplicates),
    // then store the final status and body; the probe used a ConcurrentDictionary for brevity.
    var result = await CreateOrderAsync(body);
    store.Save(composite, new(fingerprint, 201, result.Json));
    ctx.Response.Headers.Location = $"/orders/{result.Id}";
    return Results.Text(result.Json, "application/json", statusCode: 201);
});
```

`IIdempotencyStore` is your abstraction (Redis `SET NX` with TTL, or a unique index in SQL). Do the reservation and the business write in one transaction or make the write itself idempotent; otherwise a crash between "do work" and "save response" can double-apply.

---

## 5. Conditional requests and optimistic concurrency (RFC 9110 sections 8.8, 13)

1. Send a **strong `ETag`** on resources that can be updated. Compute it from the representation or a row version (`rowversion`/`xmin`, hash of the JSON); quote it: `ETag: "b11e7ba8cc7e8f80"`.
2. **Conditional GET**: client sends `If-None-Match: <etag>`; if it still matches, return `304 Not Modified` without a body (verified).
3. **Lost-update protection**: require `If-Match: <etag>` on PUT, PATCH and DELETE. The server uses strong comparison (13.1.1). Missing header: `428 Precondition Required`; mismatch: `412 Precondition Failed`; both verified below. Without this, two clients that read the same version silently overwrite each other.
4. Do not use `Last-Modified` alone for fast-changing data (one-second resolution).

```csharp
app.MapGet("/items/{id}", (string id, HttpContext ctx) =>
{
    var etag = ETagFor(doc);
    ctx.Response.Headers.ETag = etag;
    ctx.Response.Headers.CacheControl = "private, max-age=0, must-revalidate";
    return ctx.Request.Headers.IfNoneMatch.ToString() == etag ? Results.StatusCode(304) : Results.Ok(doc);
});
app.MapPut("/items/{id}", (string id, HttpContext ctx) =>
{
    var ifMatch = ctx.Request.Headers.IfMatch.ToString();
    if (ifMatch.Length == 0) return Results.Problem(statusCode: 428, title: "Precondition required",
                                                    detail: "Send If-Match with the ETag from GET.");
    if (ifMatch != ETagFor(doc)) return Results.Problem(statusCode: 412, title: "Precondition failed");
    return Results.NoContent();
});
```

Observed **(verified)**: matching `If-None-Match` gives 304; PUT without `If-Match` gives 428 `application/problem+json`; wrong `If-Match` gives 412; correct gives 204. In production parse the header as a list (`EntityTagHeaderValue` / `*`), not by string equality, and expose `ETag` through CORS (section 8).

---

## 6. Caching (RFC 9111)

1. Every cacheable response should say how: `Cache-Control: public, max-age=300` for shared data; `private` for per-user data; `no-store` for anything secret (tokens, personal data, one-time codes). Token endpoint responses must be `no-store` (RFC 6749 5.1).
2. **Authenticated requests**: RFC 9111 3.5: a shared cache MUST NOT reuse a response to a request with `Authorization` for later requests unless the response has `must-revalidate`, `public` or `s-maxage`. So never add `public` to per-user responses, and prefer `private` or `no-store` for user data.
3. `Vary` lists request headers that change the representation (`Vary: Accept-Encoding, Accept-Language`). If you vary by `Origin` for CORS, emit `Vary: Origin` (ASP.NET Core does when several origins are allowed, verified). Never `Vary` on `Authorization` as a substitute for `private`.
4. Without explicit freshness a cache MAY assign a heuristic lifetime (9111 4.2.2). Always send either `max-age` or `no-store`/`no-cache`; do not rely on defaults.
5. `no-cache` means "store but revalidate"; `no-store` means "do not store". They are not synonyms.
6. Mutating a resource does not purge arbitrary caches; use short lifetimes plus ETags rather than long `max-age` on mutable data. A non-error response to an unsafe method MUST invalidate stored responses for the target URI and MAY invalidate same-origin `Location`/`Content-Location` URIs (9111 4.4); do not rely on anything else being purged.
7. ASP.NET Core `ResponseCaching`/`OutputCache` middleware keys on the URL and `Vary`; authenticated or personalised endpoints should not be output-cached unless the policy varies by user.

---

## 7. Problem Details (RFC 9457)

Media type `application/problem+json`. Members (section 3.1):

| Member | Meaning | Rule |
|---|---|---|
| `type` | URI identifying the **problem type**; the primary identifier | prefer absolute URIs; default `about:blank`; consumers SHOULD NOT auto-dereference it |
| `title` | short summary of the type | SHOULD NOT change between occurrences (except localisation) |
| `status` | HTTP status | advisory; MUST equal the real HTTP status |
| `detail` | explanation for **this** occurrence | help the client correct the problem, not debugging information; clients SHOULD NOT parse it |
| `instance` | URI of this occurrence | may be opaque |
| extensions | any extra members | machine-readable data goes here (`errors`, `traceId`, `balance`); consumers ignore unknown members |

**Rules**
1. Return Problem Details for **every** non-2xx the API produces, including 401/403/404/405/415 and unhandled exceptions, so clients have one error shape. Register `builder.Services.AddProblemDetails()`, call `app.UseExceptionHandler()` and `app.UseStatusCodePages()` (all verified to produce `application/problem+json` for 404, 400 validation, 500).
2. **Security (RFC 9457 section 5):** vet what you put in a problem; no stack dumps, SQL, hostnames, connection strings or token validation internals. Observed **(verified)**: an unhandled `InvalidOperationException("secret connection string")` returned only the generic title and a `traceId`; keep it that way, and do not set `detail = ex.Message`.
3. Define your own problem types and document them at stable URLs (`https://api.example.com/problems/insufficient-funds`). The framework defaults point `type` at RFC 9110 sections, and some map to older documents (the 422 default pointed at an RFC 4918 section, verified); override `type` where you want stable, own identifiers.
4. Add a `traceId` (the sample uses `CustomizeProblemDetails`) or `instance` so a support case can be tied to a log entry.
5. Validation: `Results.ValidationProblem(errors)` yields `errors: { field: [messages] }` and `400` (verified). Keep field names stable (they are API); never echo secrets from rejected input.
6. Keep the `status` member equal to the HTTP status (a mismatch confuses clients and caches, RFC 9457 section 5).
7. `Content-Language` can be added when `title` is localised. Do not localise `type`.
8. For OAuth/bearer failures the primary signal is the `WWW-Authenticate` header; a Problem Details body is an addition (`oauth-oidc.md`).

```csharp
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = c =>
{
    c.ProblemDetails.Extensions["traceId"] = c.HttpContext.TraceIdentifier;
});
builder.Services.AddExceptionHandler<DomainExceptionHandler>();   // maps your exceptions to problem types
app.UseExceptionHandler();
app.UseStatusCodePages();
```

---

## 8. CORS

CORS (WHATWG Fetch Standard; not read for this file, behaviour below from running ASP.NET Core 10) lets a **browser** page from one origin read responses from another. It is a browser-enforced relaxation of the same-origin policy, **not authorization**.

**Facts to design around**
1. **CORS does not protect the server.** A request from a disallowed origin is still executed; the browser just refuses to hand the response to the page. **(verified: `/multi` with `Origin: https://c.example.com` returned 200 with no `Access-Control-Allow-Origin`.)** Curl, servers and agents ignore CORS. Authenticate and authorize every request regardless.
2. **Preflight**: for non-simple requests (custom headers such as `Authorization`, `If-Match`, `Idempotency-Key`; methods other than GET/HEAD/POST; JSON `Content-Type`) the browser first sends `OPTIONS` with `Access-Control-Request-Method` and `-Headers`. The policy must allow them. Verified: an allowed origin got `204` with `Access-Control-Allow-Origin/-Methods/-Headers/-Max-Age`; a disallowed origin got `204` with **no** CORS headers, so the browser blocks. Preflights carry no credentials: do not put authentication in front of `OPTIONS` that rejects it (`UseCors` runs before `UseAuthentication`/`UseAuthorization`).
3. **Credentials**: with cookies or `Authorization` the server must name the **exact origin** (not `*`) and send `Access-Control-Allow-Credentials: true`. ASP.NET Core refuses `AllowAnyOrigin()` plus `AllowCredentials()` and **fails at startup** with `InvalidOperationException: The CORS protocol does not allow specifying a wildcard (any) origin and credentials at the same time` (verified).
4. **`Vary: Origin`** must accompany a response whose `Access-Control-Allow-Origin` depends on the request origin, or caches serve one origin's headers to another. ASP.NET Core emitted `Vary: Origin` with a multi-origin policy (verified).
5. **Expose headers explicitly.** Script can read only CORS-safelisted response headers unless you list others; `WithExposedHeaders("ETag", "Location", "Retry-After")` (verified in the response as `Access-Control-Expose-Headers`).
6. Cache preflights with `SetPreflightMaxAge` (verified `Access-Control-Max-Age: 600`); browsers cap the value.

**Rules**
1. Use a **named policy with an explicit allow-list** loaded from configuration; apply it to the endpoints that need it (`RequireCors("web")`), not globally.
2. Never reflect the incoming `Origin` (`SetIsOriginAllowed(_ => true)`) when credentials are allowed; that turns every site into a trusted origin. Compare against an exact set, or a strict wildcard for subdomains (`SetIsOriginAllowedToAllowWildcardSubdomains` only with a vetted parent domain). Treat `Origin: null` as untrusted.
3. List only the methods and headers you need; never `AllowAnyHeader` with credentials on an authenticated API.
4. Public, anonymous, read-only data may use `AllowAnyOrigin()` without credentials.
5. For MCP over Streamable HTTP, `Origin` validation is a spec requirement and is **not** performed by the SDK; see `mcp-ai.md` section 2.
6. Prefer a server-side proxy (backend for frontend) to cross-origin credentialed browser calls.

```csharp
builder.Services.AddCors(o => o.AddPolicy("web", p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>()!)
    .WithMethods("GET", "PUT", "POST")
    .WithHeaders("Content-Type", "Authorization", "If-Match", "Idempotency-Key")
    .WithExposedHeaders("ETag", "Location", "Retry-After")
    .SetPreflightMaxAge(TimeSpan.FromMinutes(10))));
app.UseCors("web");     // before UseAuthentication / UseAuthorization
```

---

## 9. Content negotiation and media types

1. Accept `application/json` (and `application/problem+json` for errors); answer `415` for an unsupported request `Content-Type`, `406` when `Accept` cannot be satisfied; test what your framework version returns for a wrong `Content-Type`.
2. JSON is UTF-8 (RFC 8259); do not emit a BOM or non-UTF-8 charsets. Reject duplicate keys and unknown critical fields where ambiguity is dangerous, and set size limits (`api-design.md`).
3. Version through media type or URL consistently (`api-design.md`); send `Vary: Accept` if the representation depends on `Accept`.
4. Set `X-Content-Type-Options: nosniff` on API responses. Do not return JSON containing user input with `text/html`.

---

## 10. Do / Don't

**Do**
- Use GET only for safe reads; retry only idempotent methods or keyed POSTs.
- Return 201 + `Location`, 204 for empty success, 401 with `WWW-Authenticate`, 405 with `Allow`.
- Protect updates with strong ETags and `If-Match` (428 when missing, 412 on mismatch).
- Send explicit `Cache-Control`; `no-store` for secrets, `private` for per-user data.
- Return RFC 9457 problems for all errors, with your own stable `type` URIs and a trace id.
- Configure CORS by explicit origin allow-list, apply per endpoint, expose needed headers, and still authorize on the server.

**Don't**
- Put side effects behind GET; return 200 for failures; return 401 without a challenge.
- Add `Cache-Control: public` to responses carrying user data.
- Put exception messages, stack traces or internal hostnames in `detail`.
- Reflect arbitrary `Origin` values or combine wildcard origin with credentials.
- Treat CORS as access control, or `Idempotency-Key` as an RFC (it is an Internet-Draft).

---

## 11. Checklist

- [ ] Every endpoint's method matches RFC 9110 safe/idempotent semantics; no GET with side effects.
- [ ] Status codes follow section 3; 401 has `WWW-Authenticate`, 405 has `Allow`, 201 has `Location`.
- [ ] POSTs that clients retry (payments, orders) support an idempotency key with composite lookup, fingerprint check and atomic reservation, or are redesigned as PUT.
- [ ] Mutable resources send strong ETags; PUT/PATCH/DELETE require `If-Match`.
- [ ] `Cache-Control` set on every response; `no-store` on secrets; no `public` on authenticated data; `Vary` correct.
- [ ] `AddProblemDetails`, `UseExceptionHandler`, `UseStatusCodePages` configured; errors use `application/problem+json`; no internals leaked.
- [ ] Problem `type` URIs are your own, documented and stable; `status` matches the HTTP status.
- [ ] CORS: explicit origins, credentials only with exact origins, exposed headers listed, `UseCors` before authentication, server-side authorization still enforced.
- [ ] Retries (Polly/resilience handler) restricted to idempotent requests and honour `Retry-After`.
- [ ] Drafts labelled (Idempotency-Key); CORS behaviour tested in a browser as well as with curl.
