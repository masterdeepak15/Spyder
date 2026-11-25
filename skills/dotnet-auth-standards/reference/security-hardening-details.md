# Security hardening guidelines for C# / ASP.NET Core APIs
> Details file: full explanations, examples, per-section checklists and sourcing. The short must-follow rules are in [security-hardening.md](security-hardening.md); load that first. Load this file only for the section you need.

Scope: OWASP API Security Top 10 (2023) mapped to ASP.NET Core, secrets management, input validation and output encoding, security headers, dependency and supply-chain checks in CI, and a secure-defaults checklist. Auth flows are in `oauth-oidc.md` and `tokens-jwt.md`; CORS and Problem Details in `http-rest.md`; mass assignment, object-level authorization and pipeline order in `api-design.md`; TLS and HSTS in `transport-tls.md`; SSRF for webhooks in `webhooks-events.md`; rate limiting, logging and audit in `production-operations.md`.

**Sources.** Read from downloaded text in `_sources/ops/` (data only): the ten OWASP API Security Top 10 2023 pages (the "Is the API vulnerable" and "How to prevent" parts for API4 and the headline of each; the other prevention lists were skimmed, not quoted), Microsoft's "Safe storage of app secrets in development" article, the ASP.NET Core anti-request-forgery article (only its CSRF description was read), the Kestrel options article (limits). **Not read:** the OWASP Cheat Sheet Series, OWASP ASVS, the Content-Security-Policy and other header specifications, CodeQL/Dependabot/gitleaks/Trivy documentation, CycloneDX/SPDX specifications. Statements on those rest on the author's experience and are labelled **(author's recommendation)** or **(not read)**. **(verified)** means observed on a throwaway ASP.NET Core 10 app (SDK 10.0.401, Windows, run with curl, then deleted). `examples/ProductionSamples/` (`SecurityHeaders.cs`, `InputValidation.cs`) builds with 0 warnings and its selftest passes (157 checks); those samples are the source of truth for headers and validation. Blocks still marked **unverified** have no sample or test: the Kestrel limits other than `AddServerHeader`, the fallback authorization policy, the Key Vault line and the CI job.

---

## 1. OWASP API Security Top 10 (2023) mapped to ASP.NET Core

The category names are the OWASP 2023 titles. The "ASP.NET Core control" column is the author's mapping; OWASP does not name .NET features.

| # | OWASP 2023 | ASP.NET Core control | Where |
|---|---|---|---|
| API1 | Broken Object Level Authorization | Check ownership or a policy against the loaded resource on every endpoint that takes an id (`IAuthorizationService.AuthorizeAsync(user, resource, requirement)`); do not trust the id alone; prefer unguessable ids as defence in depth, not as the control | `api-design.md` section 8 |
| API2 | Broken Authentication | Standard flows, JwtBearer with pinned validation, no custom token schemes, throttle credential endpoints | `oauth-oidc.md`, `tokens-jwt.md`, rate limiting (`production-operations-details.md` section 1) |
| API3 | Broken Object Property Level Authorization | Separate input and output DTOs; allow-list bound and patched properties; never return entities | `api-design.md` sections 7, 8 |
| API4 | Unrestricted Resource Consumption | Rate limiter, Kestrel request limits, pagination caps, upload size limits, timeouts, spending limits on paid downstream APIs | section 1.1 below, `production-operations.md` |
| API5 | Broken Function Level Authorization | `RequireAuthorization(policy)` on every endpoint, deny by default with a fallback policy, admin routes in their own group with their own policy | section 1.2 |
| API6 | Unrestricted Access to Sensitive Business Flows | Per-flow throttling (sign-up, coupon, purchase), idempotency keys, step-up checks; this is a business control, not a framework switch | `production-operations-details.md` section 1, `http-rest.md` |
| API7 | Server Side Request Forgery | Allow-list outbound hosts, block private/link-local ranges after DNS resolution, no redirects, `HttpClient` with a pinned connect callback | `webhooks-events.md` (SSRF guard) |
| API8 | Security Misconfiguration | Secure-defaults checklist (section 8), headers (section 5), no developer pages or verbose errors in production, TLS (`transport-tls.md`), CORS allow-list | sections 5, 8 |
| API9 | Improper Inventory Management | One OpenAPI document per deployed version, retire old versions, list hosts and environments, no forgotten debug or staging endpoints | `api-design.md` (versioning, OpenAPI) |
| API10 | Unsafe Consumption of APIs | Treat third-party responses as untrusted input: validate, size-limit, time-out, TLS only, no blind redirect following | section 3, `production-operations-details.md` section 5 |

### 1.1 API4: set limits explicitly

OWASP's prevention list for API4 names: bound the size of all incoming parameters and payloads (string length, array length, upload size), rate-limit how often a client may call, throttle single sensitive operations (OTP validation, password recovery), validate the parameter that controls the number of records returned, and set spending limits or billing alerts on third-party services. In ASP.NET Core that becomes:

```csharp
builder.WebHost.ConfigureKestrel(k =>
{
    k.Limits.MaxRequestBodySize = 1_000_000;                    // bytes; default is 30,000,000 bytes
    k.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(15);
    k.Limits.MaxRequestHeadersTotalSize = 16 * 1024;
    k.AddServerHeader = false;                                  // do not advertise the server
});
```

Unverified (not compiled). `MaxRequestBodySize` and `RequestHeadersTimeout` are described in the Kestrel options article that was read; the default of 30,000,000 bytes is from that article; `MaxRequestHeadersTotalSize` and `AddServerHeader` were not found in it and need the compile check. Raise the body limit per endpoint (`IHttpMaxRequestBodySizeFeature`, or `[RequestSizeLimit]` on controllers) for upload routes only. A page-size cap (for example `Math.Clamp(pageSize, 1, 100)`) belongs in every list endpoint (`api-design.md`).

### 1.2 API5: deny by default

```csharp
builder.Services.AddAuthorization(o =>
{
    o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    o.AddPolicy("admin", p => p.RequireRole("admin"));
});
// public endpoints opt out explicitly:
app.MapGet("/health/live", () => "ok").AllowAnonymous();
app.MapGroup("/admin").RequireAuthorization("admin");
```

Unverified (not compiled). The fallback policy applies to endpoints that have no authorization metadata, so a forgotten endpoint is closed instead of open **(author's recommendation to rely on it; the behaviour is standard ASP.NET Core, not re-run here)**.

---

## 2. Secrets management

Microsoft's article: never store passwords or other sensitive data in source code or configuration files; production secrets must not be used for development or test; secrets are not deployed with the app; production secrets come from a controlled means such as Azure Key Vault.

Rules:
1. **Development: user secrets or environment variables.** `dotnet user-secrets init` adds a `UserSecretsId` to the project file; values live in `%APPDATA%\Microsoft\UserSecrets\<user_secrets_id>\secrets.json` on Windows (path from the article). That file is outside the repository, **unencrypted**, and not a production store.
2. **Production: a vault or the platform's secret store** (Key Vault, Kubernetes secrets mounted by a CSI driver, a cloud secret manager). Prefer a managed identity or workload identity so the app holds **no** vault credential **(author's recommendation)**.
3. **Prefer secret-free designs**: public OAuth clients have no secret (`oauth-oidc.md`); use certificates or workload identity federation for service clients; sign webhooks with per-endpoint secrets stored in the vault.
4. **No secrets in `appsettings*.json`, Dockerfiles, CI variables printed to logs, `launchSettings.json`, or connection strings committed to git.** Use placeholders and bind from configuration at runtime.
5. **Never log secrets.** Do not log `IConfiguration` dumps, full connection strings, `Authorization` headers, request bodies of token endpoints. See `production-operations-details.md` section 3 for redaction.
6. **Rotate.** Every secret has an owner and a rotation procedure; after a suspected leak, rotate first and clean git history second (history rewriting does not un-leak a public secret) **(author's recommendation)**.
7. **Bind and validate at startup.** `AddOptions<T>().BindConfiguration("Section").ValidateDataAnnotations().ValidateOnStart()` fails fast when a required secret is missing (see `production-operations-details.md` section 6).

```csharp
// Program.cs: configuration order is later-wins; vault overrides files, environment overrides both.
// Key Vault provider package: Azure.Extensions.AspNetCore.Configuration.Secrets (not installed or tested here).
// builder.Configuration.AddAzureKeyVault(new Uri(vaultUri), new DefaultAzureCredential());
var apiKey = builder.Configuration["Payments:ApiKey"]
    ?? throw new InvalidOperationException("Payments:ApiKey is not configured");
```

The vault line is a **commented, unverified** illustration; the Key Vault configuration article was not read for this file.

---

## 3. Input validation and output encoding

Rules (OWASP API3, API4, API10; the techniques are the author's recommendation unless stated):
1. **Validate at the edge, on the server, against an allow-list.** Type, length, range, format, enum membership. Client-side validation is a usability feature only.
2. **Bind to DTOs that hold only client-editable fields**; mark required members `required`; reject unknown members where mass assignment is a risk (`JsonUnmappedMemberHandling.Disallow`, see `api-design.md` section 8).
3. **Bound everything**: string lengths, collection sizes, nesting depth (`JsonSerializerOptions.MaxDepth`), body size (section 1.1), page size.
4. **Use parameterised queries** (EF Core LINQ, `FromSql` with interpolation which parameterises, Dapper parameters). Never concatenate user input into SQL, shell commands, LDAP filters, file paths or URLs.
5. **File paths**: resolve with `Path.GetFullPath` and check the result starts with the allowed root; never use a client file name as a path.
6. **Outbound URLs from user input** go through the SSRF guard (`webhooks-events.md`).
7. **Output encoding**: JSON APIs encode through `System.Text.Json`; do not build JSON or HTML by string concatenation. If you return HTML, use Razor (encodes by default) or `HtmlEncoder`. Set `Content-Type` correctly and `X-Content-Type-Options: nosniff` (section 5).
8. **Log injection**: strip or encode CR/LF in user text put in log messages; see `production-operations-details.md` section 3.
9. **Validate third-party API responses** the same way as client input (OWASP API10).
10. **Errors reveal nothing**: Problem Details with a stable `type` and no stack traces, SQL text or internal hostnames (`http-rest.md`).

```csharp
public sealed record CreateOrder(
    [property: Required, StringLength(64, MinimumLength = 1)] string Sku,
    [property: Range(1, 100)] int Quantity);

app.MapPost("/orders", (CreateOrder body) => Results.Created("/orders/1", body));
```

Minimal API validation: call `services.AddValidation()` (.NET 10) so data annotations on minimal API parameters produce a 400 problem-details response naming the member **(verified)**: `examples/ProductionSamples/InputValidation.cs` compiles, and its selftest checks quantity 0 and 1001, a missing or over-long name and a bad email, with nothing stored for invalid requests. The record above is a shorter variant of the sample's DTO. Alternatives: FluentValidation, or manual checks returning `Results.ValidationProblem`.

---

## 4. CORS, CSRF, mass assignment: pointers

- **CORS** is a browser relaxation, not authorization; allow-list exact origins, named policy, `UseCors` before authentication: `http-rest.md` section 8 (verified there).
- **CSRF** matters only when authentication is **ambient** (cookies, Windows auth). Microsoft's description: browsers store cookies and send them automatically with requests to that domain, so a cookie-authenticated site trusts any request with a valid cookie. A pure bearer-token API (token in the `Authorization` header, not a cookie) is not CSRF-prone for that reason. If you use cookies: SameSite plus antiforgery tokens (`AddAntiforgery`, `UseAntiforgery`), and never state-changing GETs **(author's recommendation, antiforgery article read only for the threat description)**. Browser clients of an OAuth API should use the backend-for-frontend pattern with cookies and CSRF protection, or tokens in memory, per `oauth-oidc.md`.
- **Mass assignment** and object-property authorization: `api-design.md` section 8.

---

## 5. Security headers

For an API (JSON only) a small set is enough; for anything serving HTML, add CSP tuned to the page. The header values below are the author's recommendation; the header specifications were not read.

| Header | Value for a JSON API | Why |
|---|---|---|
| `X-Content-Type-Options` | `nosniff` | Stops browsers guessing a content type |
| `Cache-Control` | `no-store` for authenticated or personal responses | Keep tokens and PII out of shared and browser caches (`http-rest.md` caching) |
| `Content-Security-Policy` | `default-src 'none'; frame-ancestors 'none'` | An API returns no active content; blocks framing |
| `Referrer-Policy` | `no-referrer` | No URL leakage |
| `Strict-Transport-Security` | `max-age=31536000; includeSubDomains` | HTTPS only, see `transport-tls.md` section 3 |
| (remove) `Server` | disabled via `AddServerHeader = false` | Less fingerprinting |

```csharp
app.Use(async (ctx, next) =>
{
    ctx.Response.OnStarting(() =>
    {
        var h = ctx.Response.Headers;
        h["X-Content-Type-Options"] = "nosniff";
        h["Referrer-Policy"] = "no-referrer";
        h["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
        h["Cache-Control"] = "no-store";
        return Task.CompletedTask;
    });
    await next();
});
```

**(verified)** this middleware, in a throwaway app, produced the four headers on a `200` response (seen with `curl -D -`). Setting headers in `OnStarting` avoids "response already started" errors. `Cache-Control: no-store` here applies to every response including ones you want cached; narrow it per endpoint for public cacheable data. The full sample is planned as `examples/ProductionSamples/SecurityHeaders.cs` (not yet reported).

---

## 6. Dependency and supply-chain scanning in CI

Tools below are named from the author's experience; their documentation was not read for this file, and none of the commands were run here except where stated.

| Control | Tool | Notes |
|---|---|---|
| Vulnerable NuGet packages | `dotnet list package --vulnerable --include-transitive` | Reads the NuGet vulnerability data (GitHub Advisory Database via the NuGet source). Fail the build when the output lists a vulnerable package; also consider `NuGetAudit` in the project file (`<NuGetAudit>true</NuGetAudit>`, `<NuGetAuditMode>all</NuGetAuditMode>`) so restore warns **(not run here)** |
| Outdated packages | `dotnet list package --outdated`; Dependabot or Renovate pull requests | Update on a schedule, not only on alerts |
| Static analysis (SAST) | CodeQL for C# (GitHub code scanning) or an equivalent; built-in .NET analyzers (`AnalysisLevel`, `EnableNETAnalyzers`, security rules CA2100, CA3xxx, CA5xxx) | Treat security rule warnings as errors |
| Secret scanning | GitHub secret scanning with push protection, and/or `gitleaks` in CI and as a pre-commit hook | Scan history once when adopting; rotate anything found |
| SBOM | CycloneDX (`CycloneDX` .NET tool) or SPDX generated in CI, stored with the build artifact | Lets you answer "are we affected by CVE-X" quickly |
| Container image | Trivy, Grype or the registry's scanner on the built image; use a minimal or chiseled base image, non-root user, pinned digests | Scan on build and on a schedule, because new CVEs appear after release |
| Lock the graph | `RestorePackagesWithLockFile` + `--locked-mode` in CI; `packageSourceMapping` so internal package names cannot come from the public feed | Reduces dependency-confusion risk |
| Pipeline | Pin third-party CI actions by commit SHA; least-privilege tokens; no secrets in PR builds from forks | |

```yaml
# Sketch only, unverified. A job that fails on vulnerable packages.
- run: dotnet restore --locked-mode
- run: |
    dotnet list package --vulnerable --include-transitive 2>&1 | tee audit.txt
    if grep -q "has the following vulnerable packages" audit.txt; then exit 1; fi
```

The grep text depends on the SDK's output wording; verify it on your SDK before relying on it. Prefer `NuGetAuditMode`/`WarningsAsErrors` for NU1901 to NU1904 **(not run here)**.

---

## 7. Secure defaults for the host

1. **Production error handling**: `UseExceptionHandler` plus `AddProblemDetails`; no `UseDeveloperExceptionPage` outside Development (`transport-tls.md` rule 6).
2. **HTTPS**: TLS 1.2+1.3 policy, HSTS, no plain-HTTP listener for APIs.
3. **Forwarded headers** only from known proxies (`transport-tls.md`).
4. **Disable what you do not use**: Swagger/OpenAPI UI and `/openapi` in production unless protected, gRPC reflection, diagnostic endpoints, sample endpoints.
5. **Health and metrics endpoints** are not public by default (`production-operations-details.md` section 4).
6. **Run as non-root** in containers, read-only file system where possible, no secrets in image layers.

---

## 8. Checklist

OWASP mapping
- [ ] Every endpoint taking an id checks the caller's right to that object (API1); every endpoint has an authorization policy or an explicit `AllowAnonymous` (API5, fallback policy set).
- [ ] Input and output DTOs are separate; no entity is bound or returned directly (API3).
- [ ] Request body size, header size and timeouts, page size and upload size are bounded; rate limits exist, stricter on credential, OTP and business-flow endpoints (API4, API6).
- [ ] Outbound calls from user-supplied URLs go through an SSRF guard (API7); third-party responses are validated and time-limited (API10).
- [ ] An inventory of deployed API versions and hosts exists; retired versions are removed (API9).

Secrets
- [ ] No secret in source, `appsettings*.json`, container images or logs; user secrets for development, a vault or platform store for production; rotation owner named.
- [ ] Required secrets validated at startup (`ValidateOnStart`).

Input and output
- [ ] Server-side validation with allow-lists; parameterised queries; file paths and URLs from users validated.
- [ ] No string-built JSON or HTML; CR/LF removed from user text in logs.

Headers and browser
- [ ] `nosniff`, CSP, `Referrer-Policy`, HSTS, `Cache-Control` for sensitive responses; `Server` header off.
- [ ] CORS allow-list and CSRF protection if cookies are used (see section 4).

Supply chain
- [ ] CI fails on vulnerable packages, runs SAST and secret scanning, produces an SBOM, scans the container image, uses lock files and package source mapping.

Host
- [ ] No developer exception page, development certificate or Swagger UI exposed in production; non-root container.
