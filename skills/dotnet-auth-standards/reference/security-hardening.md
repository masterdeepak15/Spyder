# Security hardening rules for C# / ASP.NET Core APIs

Must-follow rules only. OWASP mapping table, code blocks, sourcing and what was verified: [security-hardening-details.md](security-hardening-details.md) (section numbers match it). Samples: `examples/ProductionSamples` (`SecurityHeaders.cs`, `InputValidation.cs`). Items without a sample or test are unverified there: Kestrel limits other than `AddServerHeader`, fallback policy, Key Vault line, CI job. Header values and scanning tools are the author's recommendation (specs and tool docs not read).

## 1. OWASP API Top 10 2023 ([§1](security-hardening-details.md#1-owasp-api-security-top-10-2023-mapped-to-aspnet-core))
- API1 object level: every endpoint that takes an id checks the caller's right to the loaded object (`IAuthorizationService.AuthorizeAsync(user, resource, requirement)`); unguessable ids are defence in depth only.
- API2 authentication: standard flows, JwtBearer with pinned validation, no custom token schemes, throttle credential endpoints.
- API3 property level: separate input and output DTOs; allow-list bound and patched properties; never return entities.
- API4 resource consumption: bound body, header, upload and page size and timeouts; rate limiter; spending limits on paid downstream APIs ([§1.1](security-hardening-details.md#11-api4-set-limits-explicitly): Kestrel `MaxRequestBodySize`, `RequestHeadersTimeout`, `AddServerHeader = false`; raise body limit per upload endpoint only; cap page size).
- API5 function level: fallback policy `RequireAuthenticatedUser()`, `RequireAuthorization(policy)` on groups, public endpoints opt out with `AllowAnonymous` ([§1.2](security-hardening-details.md#12-api5-deny-by-default)).
- API6 business flows: per-flow throttling (sign-up, coupon, purchase), idempotency keys, step-up checks.
- API7 SSRF: allow-list outbound hosts, block private and link-local ranges after DNS resolution, no redirects, pinned connect callback (see webhooks SSRF guard).
- API8 misconfiguration: section 7 defaults, headers, no developer pages in production, CORS allow-list.
- API9 inventory: one OpenAPI document per deployed version, retire old versions, no forgotten debug or staging endpoints.
- API10 unsafe consumption: third-party responses are untrusted input: validate, size-limit, time out, TLS only, no blind redirects.

## 2. Secrets ([§2](security-hardening-details.md#2-secrets-management))
- Never in source, `appsettings*.json`, `launchSettings.json`, Dockerfiles, image layers, committed connection strings, CI logs.
- Development: user secrets or environment variables (user-secrets file is unencrypted, outside the repo, not a production store). Production: vault or platform secret store; managed or workload identity so the app holds no vault credential.
- Prefer secret-free designs: public OAuth clients have no secret; certificates or workload identity for service clients; per-endpoint webhook secrets in the vault.
- Never log secrets (`IConfiguration` dumps, connection strings, `Authorization`, token endpoint bodies).
- Every secret has an owner and rotation procedure; after a leak rotate first, clean history second.
- Bind and validate at startup: `AddOptions<T>().BindConfiguration("Section").ValidateDataAnnotations().ValidateOnStart()`.

## 3. Input and output ([§3](security-hardening-details.md#3-input-validation-and-output-encoding))
- Validate on the server at the edge against allow-lists (type, length, range, format, enum); client validation is usability only.
- Bind to DTOs with client-editable fields only; `required` members; reject unknown members where mass assignment matters (`JsonUnmappedMemberHandling.Disallow`).
- Bound string length, collection size, nesting (`MaxDepth`), body size, page size.
- Parameterised queries only; never concatenate user input into SQL, shell, LDAP, paths or URLs. File paths: `Path.GetFullPath` and check the allowed root; never use a client file name as a path.
- User-supplied outbound URLs go through the SSRF guard.
- JSON via `System.Text.Json`, HTML via Razor or `HtmlEncoder`; no string-built JSON or HTML; correct `Content-Type` plus `nosniff`.
- Strip CR/LF from user text in logs. Errors are Problem Details with a stable `type`; no stack traces, SQL or internal hostnames.
- Minimal APIs: `services.AddValidation()` (.NET 10) with data annotations gives a 400 problem-details response (verified in `InputValidation.cs`).

## 4. CORS, CSRF, mass assignment ([§4](security-hardening-details.md#4-cors-csrf-mass-assignment-pointers))
- CORS is a browser relaxation, not authorization: exact-origin allow-list, named policy, `UseCors` before authentication.
- CSRF matters only with ambient auth (cookies, Windows auth): SameSite plus antiforgery (`AddAntiforgery`, `UseAntiforgery`), no state-changing GET. Bearer-header APIs are not CSRF-prone. Browser clients: BFF with cookies, or tokens in memory.

## 5. Security headers ([§5](security-hardening-details.md#5-security-headers))
- JSON API set, written in `OnStarting`: `X-Content-Type-Options: nosniff`; `Referrer-Policy: no-referrer`; `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'`; `Cache-Control: no-store` for authenticated or personal responses (narrow per endpoint for public cacheable data); `Strict-Transport-Security: max-age=31536000; includeSubDomains`; `Server` header off.

## 6. CI supply chain ([§6](security-hardening-details.md#6-dependency-and-supply-chain-scanning-in-ci))
- CI fails on `dotnet list package --vulnerable --include-transitive` (or NU1901 to NU1904 as errors; verify the grep text on your SDK); outdated packages tracked by Dependabot/Renovate.
- SAST (CodeQL, .NET analyzers, security rules as errors); secret scanning with push protection or gitleaks; SBOM (CycloneDX/SPDX); container image scan, non-root, minimal base, pinned digests.
- `RestorePackagesWithLockFile` + `--locked-mode`; `packageSourceMapping`; pin third-party CI actions by SHA; least-privilege tokens; no secrets in fork PR builds.

## 7. Secure host defaults ([§7](security-hardening-details.md#7-secure-defaults-for-the-host))
- `UseExceptionHandler` + `AddProblemDetails`; no `UseDeveloperExceptionPage` outside Development.
- TLS 1.2+1.3, HSTS, no plain-HTTP API listener; forwarded headers only from known proxies.
- Disable what is unused: Swagger/OpenAPI UI and `/openapi` in production unless protected, gRPC reflection, diagnostic and sample endpoints; health and metrics not public by default.
- Containers: non-root, read-only file system where possible, no secrets in layers.

## Review
Per-area checklist: [../checklists/security-ops.md](../checklists/security-ops.md) (shared with production operations).
