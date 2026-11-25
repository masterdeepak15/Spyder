# OAuth 2.0 / 2.1 guidelines for C# / ASP.NET Core
> Details file: full explanations, examples, per-section checklists and sourcing. The short must-follow rules are in [oauth-oidc.md](oauth-oidc.md); load that first. Load this file only for the section you need.

Scope: RFC 6749, 6750, 7636, 9700, OAuth 2.1 draft, 8693, 7523, 8707, 8414, 9728, 7591, 7662, 7009, 8628, 9449.
OIDC Core/Discovery, JWT/JWS/JWK and RFC 9068 are in `tokens-jwt.md` (next step); transport (8446, 8705) in `transport-tls.md`.

Samples are in `examples/OAuthSamples/` (net10.0, `Microsoft.AspNetCore.Authentication.JwtBearer` and `.OpenIdConnect` 10.0.15). Run `dotnet build` and `dotnet run -- selftest` there.

## Contents
1. [Baseline: what to build and what to refuse](#1-baseline)
2. [RFC 6749 core framework](#2-rfc-6749)
3. [RFC 6750 bearer tokens](#3-rfc-6750)
4. [RFC 7636 PKCE](#4-rfc-7636-pkce)
5. [RFC 9700 Security BCP and OAuth 2.1](#5-rfc-9700-and-oauth-21)
6. [RFC 8707 resource indicators](#6-rfc-8707)
7. [RFC 8414 AS metadata and RFC 9728 resource metadata](#7-rfc-8414-and-9728)
8. [RFC 7591 dynamic client registration](#8-rfc-7591)
9. [RFC 8693 token exchange and RFC 7523 JWT grants](#9-rfc-8693-and-7523)
10. [RFC 7662 introspection and RFC 7009 revocation](#10-rfc-7662-and-7009)
11. [RFC 8628 device grant](#11-rfc-8628)
12. [RFC 9449 DPoP](#12-rfc-9449-dpop)

---

## 1. Baseline

| Use case | Grant to use | Never |
|---|---|---|
| Web app / SPA / native with a user | Authorization code + PKCE (S256) | Implicit (`response_type=token`), ROPC (password grant) |
| Service to service, no user | Client credentials | Sharing a user token downstream |
| Input-constrained device | Device grant (8628) | Embedding client secrets in the device |
| Service acting for a user toward another API | Token exchange (8693) | Forwarding the inbound token as is |

---

## 2. RFC 6749

**Rules**
1. Treat the AS and RS as separate roles. An API validates tokens; it never issues them unless it is the AS.
2. Distinguish client types: a confidential client can hold a secret (server-side web app); a public client cannot (SPA, native, device). Never ship a `client_secret` in a public client. The `client_id` alone never authenticates a client (2.2, 2.3).
3. Send client credentials by one method per request. RFC 6749 2.3.1 makes HTTP Basic (`client_secret_basic`) mandatory for an AS; the OAuth 2.1 draft reverses this: credentials in the form body (`client_secret_post`) MUST be supported and Basic is only MAY, because the form-urlencode-then-Base64 step caused interoperability bugs. If you use Basic, form-urlencode id and secret first (`WebUtility.UrlEncode`, see `TokenClient.Basic`). Prefer asymmetric methods (mTLS, `private_key_jwt`) where possible (2.1 recommends them).
4. Token requests are POST with `application/x-www-form-urlencoded` over TLS. Token responses carry `Cache-Control: no-store` (5.1). Parameters must not repeat and unknown ones are ignored.
5. Authorization codes are single use and short-lived (10 minutes maximum recommended, 4.1.2). A second use must be denied and the tokens issued from that code should be revoked. RFC 6749 4.1.3: `redirect_uri` is required at the token endpoint only if it was sent in the authorization request, and the values must be identical. OAuth 2.1 drops `redirect_uri` from the token request (an AS serving both must still accept and enforce it for 2.0 clients).
6. An unauthenticated (public) client must send `client_id` at the token endpoint so it cannot accept a code issued to another client (3.2.1). The client credentials grant is for confidential clients only (4.4).
7. Redirect URIs: RFC 6749 3.1.2.2 requires registration for public clients but still allows registering only scheme, authority and path (varying the query). Do not rely on that: RFC 9700 and OAuth 2.1 require the complete URI and exact matching (section 5). The AS must never redirect to an unregistered or mismatching URI; it shows the error to the user instead (3.1.2.4).
8. Handle errors by code: `invalid_request`, `invalid_client` (401 plus `WWW-Authenticate` if the client used the Authorization header), `invalid_grant`, `unauthorized_client`, `unsupported_grant_type`, `invalid_scope`.
9. Refresh (6): requested scope must not exceed the original grant; if a new refresh token is issued, discard the old one and keep its scope identical. Refresh tokens are bound to the issuing client; token and refresh responses (anything with tokens) carry `Cache-Control: no-store` (and `Pragma: no-cache` in 6749).

**Example**: client credentials request with Basic auth and an audience-restricted token: `TokenClient.ClientCredentialsForm` plus `TokenClient.PostFormAsync` (see section 6).

**Review checklist**
- [ ] No implicit or password grant anywhere (code, config, AS client registrations).
- [ ] No secrets in source, appsettings committed to git, or public clients.
- [ ] Token endpoint calls use POST, form encoding, HTTPS; errors are mapped from the `error` code, not from the message text.
- [ ] Old refresh token is replaced when a new one is returned.

---

## 3. RFC 6750

**Rules**
1. Send tokens in `Authorization: Bearer <token>`. RS must support this. Query-string tokens: RFC 6750 2.3 only says SHOULD NOT, RFC 9700 4.3.2 says clients MUST NOT, and the OAuth 2.1 draft (5.1) says the RS MUST ignore a token in the query string. Do not use the form-body method unless the client cannot set headers (and only POST with `application/x-www-form-urlencoded`, ASCII, single-part). The scheme name `Bearer` is case-insensitive.
2a. Treat access tokens as opaque in the client (2.1 1.4: the client MUST NOT expect to parse them). The RS must not cap token length at small values; JWT access tokens routinely exceed 1 KB (2.1 1.7.1). Mind proxy and Kestrel header limits.
2. Use one token transmission method per request.
3. On failure the RS returns `WWW-Authenticate: Bearer ...`. No credentials sent: no `error` code. Token present but bad: `invalid_token` with 401. Token valid but too little scope: `insufficient_scope` with 403 (optionally `scope="..."`). Malformed request: `invalid_request` with 400.
4. `error`, `scope` and `realm` attributes must not repeat.
5. Tokens are secrets: TLS only, never logged, never in URLs, short lifetime (one hour or less recommended), audience-restricted.

**Example** (`ResourceServer.AddApiAuth`): the JwtBearer handler plus `OnChallenge`/`OnForbidden` that emit the compliant header. Live check on the sample: no token returned `401` with `WWW-Authenticate: Bearer resource_metadata="..."`; a bad token returned `Bearer error="invalid_token", resource_metadata="..."`.

```csharp
OnChallenge = ctx =>
{
    ctx.HandleResponse();
    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
    var error = ctx.AuthenticateFailure is null ? "" : "error=\"invalid_token\", ";
    ctx.Response.Headers.WWWAuthenticate = $"Bearer {error}resource_metadata=\"{metadataUrl}\"";
    return Task.CompletedTask;
}
```

**Review checklist**
- [ ] `Authorization` header only; no `access_token=` query parameter, no tokens in logs or URLs.
- [ ] 401 vs 403 used correctly; `WWW-Authenticate` present on both.
- [ ] Do not echo token validation exception text to the caller.
- [ ] `ValidateAudience` is true and `ValidAudience` is this API.

---

## 4. RFC 7636 PKCE

**Rules**
1. Public clients must use PKCE; confidential clients should too (9700). OAuth 2.1 requires it for the code flow.
2. Use `S256` only: `challenge = BASE64URL(SHA256(ASCII(verifier)))`. `plain` is for clients that cannot hash, and must never be used after an `S256` attempt (7.2). OAuth 2.1 removes `plain`.
3. Verifier: 43 to 128 characters from `[A-Z a-z 0-9 - . _ ~]`; generate 32 random bytes with `RandomNumberGenerator` and base64url them (43 chars, 256 bits). Never use `Random` or a timestamp.
4. A new verifier per authorization request; keep it server-side or in the encrypted state tied to the user agent.
5. AS side: bind challenge and method to the code, require `code_verifier` at the token endpoint, mismatch gives `invalid_grant`. Reject a `code_verifier` when the authorization request had no `code_challenge` (downgrade defence, 9700 4.8.2); the 2.1 draft (3.2.4) lists this case under `invalid_request`. Never put `code_challenge` into a response value others can read.
5a. OAuth 2.1 draft 4.1.1 makes `code_challenge_method` REQUIRED (RFC 7636 defaults it to `plain` when missing, which is why you always send `S256` explicitly) and requires the AS to reject requests without `code_challenge` from public clients.
6. In ASP.NET Core `AddOpenIdConnect`, `UsePkce = true` (explicitly set, as in `OidcClient.cs`) with `ResponseType = Code`.

**Example** (`Pkce.cs`, verified against the RFC 7636 Appendix B vector in `selftest`):

```csharp
var verifier  = Base64Url(RandomNumberGenerator.GetBytes(32));
var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
// authorize request: code_challenge=<challenge>&code_challenge_method=S256
```

**Review checklist**
- [ ] `code_challenge_method=S256` on every authorization request.
- [ ] Verifier from a CSPRNG, 43+ chars, fresh per request.
- [ ] AS enforces PKCE and rejects verifier without challenge; publishes `code_challenge_methods_supported`.

---

## 5. RFC 9700 and OAuth 2.1

OAuth 2.1 is an Internet-Draft (`draft-ietf-oauth-v2-1-17` per datatracker at time of writing); it consolidates 6749, 6750 and 9700. It is not a final RFC: cite it as a draft.

**Rules**
1. Redirect URIs: the AS compares by exact string match (loopback IP redirects may vary the port). No wildcards, no prefix match, no open redirectors. No `http` redirect URIs except loopback. See `Metadata.RedirectUriAllowed`.
2. Do not use implicit or ROPC. 2.1 removes both.
3. CSRF and code injection: PKCE, or `nonce` in OIDC, or one-time `state` bound to the user agent. If `state` carries data, protect it against tampering.
4. Mix-up: a client talking to more than one AS must store the expected issuer per request and compare it with the `iss` response parameter (RFC 9207) or the ID token `iss`; abort on mismatch.
5. Sender-constrain access tokens (mTLS or DPoP) where you can; audience-restrict them always. Public-client refresh tokens must be sender-constrained or rotated (one-time use).
6. Authorization codes are invalidated after first use. Do not use HTTP 307 for redirects that carry credentials; prefer 303 (2.1 1.6 excludes 307 for all OAuth redirections).
6a. The 2.1 draft (4.1.2) makes the `iss` parameter REQUIRED in every authorization response, success and error (RFC 9207). Check it against the issuer you started with.
6b. CORS: the authorization endpoint MUST NOT support CORS (the browser is redirected, not scripted). Token, metadata, registration, revocation and introspection endpoints must send CORS headers when browser-based apps call them (2.1 3.1, 3.2). A browser app that needs a client secret should use a backend (BFF) instead.
6c. All OAuth URLs must be https; only loopback redirect URIs may be http (2.1 1.5). Do not put authorization-response parameters in the redirect URI query registered with the AS.
7. Refresh tokens bind to the scope and resource servers the owner consented to and should expire after inactivity; revoke on security events.

**Example** (`Metadata.RedirectUriAllowed`, tested: exact match passes; prefix, subdomain and other port are rejected; loopback with any port passes):

```csharp
if (string.Equals(registered, requested, StringComparison.Ordinal)) return true;
```

**Review checklist**
- [ ] Registered redirect URIs are full URIs; no wildcards; compared with `StringComparison.Ordinal`.
- [ ] `response_type` is `code` only.
- [ ] Issuer stored and compared when multiple AS are used.
- [ ] Refresh token rotation or DPoP binding for public clients.
- [ ] Nothing says "OAuth 2.1 requires X" without noting it is a draft.

---

## 6. RFC 8707

**Rules**
1. Send `resource=<absolute URI of the API>` on authorization and token requests so the AS can audience-restrict the access token. The URI must be absolute, without fragment, preferably without query; use the API base URI.
2. Prefer one `resource` per request; multiple audiences require high trust between those services.
3. The AS returns `invalid_target` for an unknown or unacceptable resource; handle that code.
4. The RS must check that `aud` contains its own identifier and reject other tokens, even if the signature is valid.
5. For multi-tenant APIs use a resource URI that includes the tenant.

**Example** (`TokenClient.ClientCredentialsForm`, tested): `grant_type=client_credentials&scope=orders:read&resource=https://api.example`. The RS side is `ValidAudience` in `ResourceServer.cs`.

**Review checklist**
- [ ] Every client token request names its `resource`.
- [ ] RS audience check configured and tested with a token for another audience.
- [ ] `invalid_target` handled.

---

## 7. RFC 8414 and 9728

**Rules**
1. AS metadata lives at `/.well-known/oauth-authorization-server` with the path inserted between host and issuer path (`https://example.com/issuer1` gives `/.well-known/oauth-authorization-server/issuer1`). OIDC Discovery 4.1 differs: it APPENDS `/.well-known/openid-configuration` to the issuer (`https://example.com/issuer1` gives `https://example.com/issuer1/.well-known/openid-configuration`). Same issuer, two URL shapes; see `Metadata.OidcConfiguration`.
2. Required AS fields: `issuer` (https, no query/fragment) and `response_types_supported`; `authorization_endpoint` and `token_endpoint` unless the grants make them unnecessary.
3. The `issuer` in the document must be identical to the identifier used to build the URL; otherwise discard the document. Never normalise it. `Metadata.IdentifierMatches`.
4. Protected resource metadata (9728): `GET /.well-known/oauth-protected-resource[/path]`, required field `resource`, plus `authorization_servers`, `scopes_supported`, `bearer_methods_supported` as useful. Its `resource` value must be identical to the identifier used to build the URL.
5. Advertise the metadata URL in the 401 challenge: `WWW-Authenticate: Bearer resource_metadata="https://.../.well-known/oauth-protected-resource"`. When a client sees a new challenge it should refetch.
6. Metadata responses are 200 `application/json`; ignore unknown members; omit members with empty values. Fetch only over HTTPS with certificate validation.
7. Do not hard-code endpoints when metadata exists; let `Authority` discovery do it. Cache with a TTL.

**Example** (`Metadata.WellKnown`; tested with the RFC 8414 and 9728 examples):

```csharp
Metadata.WellKnown(new Uri("https://example.com/issuer1"), "oauth-authorization-server")
// -> https://example.com/.well-known/oauth-authorization-server/issuer1
```

The `MapProtectedResourceMetadata` extension serves the document; the sample returned HTTP 200 with the JSON on `/.well-known/oauth-protected-resource/api`.

**Review checklist**
- [ ] Well-known URL built with path insertion, not string append.
- [ ] `issuer`/`resource` compared for exact equality with the requested identifier.
- [ ] 401 responses include `resource_metadata`.
- [ ] No hard-coded token or authorize endpoints.

---

## 8. RFC 7591

**Rules**
1. Registration is `POST` of `application/json` to the registration endpoint over TLS; success is `201 Created` with `client_id` and all registered metadata; errors are 400 with `invalid_redirect_uri`, `invalid_client_metadata`, `invalid_software_statement` or `unapproved_software_statement`.
2. Redirect-based grants require `redirect_uris`; for public clients use `token_endpoint_auth_method: "none"`. Allowed redirect URIs: https, loopback http, or an app-specific scheme. Reject anything else with `invalid_redirect_uri`.
3. `jwks` and `jwks_uri` must not both be present. Unknown metadata fields are ignored. Keep `grant_types` and `response_types` consistent.
4. If a secret is issued it is unique per client and `client_secret_expires_at` is required (`0` means no expiry). Store it hashed; show it once.
5. Open registration is a risk surface: rate-limit, validate every field (RFC 7591 treats metadata as self-asserted), and check that `logo_uri`, `client_uri` and similar match the redirect host where policy requires.

**Example** (request body a client sends):

```json
{ "client_name": "Sample CLI", "redirect_uris": ["http://127.0.0.1/cb"],
  "grant_types": ["authorization_code"], "response_types": ["code"],
  "token_endpoint_auth_method": "none" }
```

**Review checklist**
- [ ] Registration endpoint is rate-limited and validates redirect URIs per section 5.
- [ ] Secret never logged; stored hashed.
- [ ] Inconsistent grant/response type combinations rejected.

---

## 9. RFC 8693 and 7523

**Token exchange (8693)**
1. POST to the token endpoint, `grant_type=urn:ietf:params:oauth:grant-type:token-exchange`, required `subject_token` and `subject_token_type`; optional `actor_token` (then `actor_token_type` is required), `resource`, `audience`, `scope`, `requested_token_type`.
2. Use exchange for delegation or impersonation toward a downstream API, asking for that API as `audience`/`resource`; never forward the inbound token.
3. The AS must validate the subject (and actor) token and answers `invalid_request` when invalid; `invalid_target` when it cannot issue for the target. Authenticate the calling client.
4. Response: 200 with `access_token`, `issued_token_type`, `token_type` (`N_A` if not an access token); `scope` required when different from requested.
5. In issued JWTs `act` records the current actor; consumers must only consider the top-level claims and the current actor, not nested prior actors for authorization.

**JWT grants and client auth (7523)**
1. Authorization grant: `grant_type=urn:ietf:params:oauth:grant-type:jwt-bearer` with a single `assertion`. Client authentication: `client_assertion_type=urn:ietf:params:oauth:client-assertion-type:jwt-bearer` with `client_assertion`.
2. The AS must require `iss`, `sub`, `aud`, `exp`; `aud` must contain the AS identity (the token endpoint URL is acceptable); for client authentication `sub` must equal the `client_id`. Check signature; reject expired tokens with clock skew allowance.
3. Use a short `exp` (minutes) and unique `jti`; track `jti` for replay protection (optional in the RFC, do it).
4. Errors: `invalid_grant` for a bad assertion grant; `invalid_client` for bad client assertion.
5. Prefer `private_key_jwt` (asymmetric) to shared secrets; never use `alg: none`.

**Example** (`TokenClient.TokenExchangeForm`): builds the exchange body (subject token type access_token, audience of the downstream API); send it with `TokenClient.PostFormAsync` and client authentication.

**Review checklist**
- [ ] Downstream calls use an exchanged token with the downstream audience.
- [ ] Assertions are short-lived, carry `aud` of the AS, `jti` tracked.
- [ ] `act`/`may_act` not trusted beyond top-level plus current actor.
- [ ] Exchange requires authenticated clients and a policy of who may exchange for what.

---

## 10. RFC 7662 and 7009

**Introspection (7662)**
1. POST `token` (+ optional `token_type_hint`) as form data over TLS; the caller must be authenticated (client auth or bearer) and ideally authorised to introspect.
2. Response JSON requires `active` (boolean). Treat anything other than `active: true` as rejection. For inactive, unknown or not-permitted tokens the AS returns `{"active": false}` and nothing else, with 200 (not an error).
3. When active, still check `aud`, `scope`, `exp`, `iss` from the response against your own requirements; introspection tells you the AS state, not that the token is for you.
4. Caching: a response may be cached but never beyond `exp`; keep TTLs short so revocation takes effect.
5. Use introspection for opaque tokens; for JWT access tokens validate locally unless you need immediate revocation.

**Revocation (7009)**
1. POST `token` (+ `token_type_hint` `access_token`/`refresh_token`) with client authentication. Servers must support refresh token revocation; access token revocation is optional. Revoking a refresh token should invalidate access tokens from the same grant.
2. HTTP 200 for success and for unknown or already-invalid tokens: do not treat 200 as proof the token existed. Error `unsupported_token_type` if the type cannot be revoked. On 503 assume the token still exists and retry later.
3. Call revocation on logout and when a refresh token is replaced or compromised.

**Example** (`TokenClient.IsActiveAsync` and `RevokeAsync`): `active` must be literally `true`; revoke uses Basic client auth and `EnsureSuccessStatusCode`.

**Review checklist**
- [ ] Introspection endpoint requires authentication and rate limiting.
- [ ] Caller checks `active == true` and also `aud`/`scope`.
- [ ] Introspection cache bounded by `exp` and a short TTL.
- [ ] Logout path calls revocation; client does not use a token after a 200 from revoke.

---

## 11. RFC 8628

**Rules**
1. Device requests `POST` `client_id` (+ `scope`) to the device authorization endpoint; response has `device_code`, `user_code`, `verification_uri`, `expires_in`, optional `verification_uri_complete`, `interval`.
2. Show `user_code` and `verification_uri` (or a QR code of `verification_uri_complete`); never show `device_code`.
3. Poll the token endpoint with `grant_type=urn:ietf:params:oauth:grant-type:device_code`. Wait at least `interval` seconds (5 if absent). `authorization_pending`: continue. `slow_down`: add 5 seconds to the interval for this and later requests. Any other error (`access_denied`, `expired_token`) stops polling. On timeouts back off.
4. AS side: high-entropy `device_code`, user codes with enough entropy and attempt rate limiting, confirm the user is authorizing the right device.
5. Public client: do not embed a secret; confidential clients must still authenticate.

**Example** (`TokenClient.PollDeviceAsync`) implements exactly rule 3.

**Review checklist**
- [ ] Interval honoured, +5 seconds on `slow_down`, stop on terminal errors.
- [ ] `user_code` brute-force limited server-side.
- [ ] Cancellation token wired to the polling loop.

---

## 12. RFC 9449 DPoP

**Rules**
1. Client creates a key pair (EC P-256 / ES256 recommended), sends a `DPoP` header (one JWT) on the token request and on every resource request. AS returns `token_type: DPoP` and binds the token via `cnf.jkt` (the RFC 7638 thumbprint of the public key).
2. Proof header: `typ: dpop+jwt`, asymmetric `alg` (never `none` or HMAC), `jwk` holding the public key only. Claims: `jti` (unique per request, at least 96 bits random or UUID v4), `htm` (method), `htu` (URL without query and fragment), `iat`, plus `ath` (base64url SHA-256 of the access token) on resource requests and `nonce` when the server supplied one.
3. Resource requests use `Authorization: DPoP <token>` plus the proof. A new proof for each request, including retries.
4. The verifier checks: at most one `DPoP` header, valid signature with the `jwk`, `typ`, `htm`/`htu`, `iat` window (seconds to minutes), `ath`, that the proof key thumbprint equals the token's `cnf.jkt`, and `jti` replay (store within the window). An RS that accepts both schemes must reject a DPoP-bound token presented as Bearer.
5. Nonces: on `use_dpop_nonce` (400 from AS, or 401 with `WWW-Authenticate: DPoP error="use_dpop_nonce"` from RS) the client reads `DPoP-Nonce`, and retries with a new proof containing it. Use the newest nonce.
6. Errors: `invalid_dpop_proof`, `use_dpop_nonce`. The AS advertises `dpop_signing_alg_values_supported`. Keep the private key non-exportable where the platform allows.
7. Binding the code to the key with `dpop_jkt` on the authorization request is optional but strengthens code flow.

**Example** (`Dpop.cs`; `selftest` builds a proof and verifies the signature, `htu`, `ath` and `typ` independently):

```csharp
using var dpop = new DpopSigner();
var proof = dpop.CreateProof("GET", new Uri("https://api.example/orders?x=1"), accessToken);
request.Headers.Authorization = new("DPoP", accessToken);
request.Headers.Add("DPoP", proof);
```

Server-side DPoP validation is not implemented in the samples, and this skill makes no claim about built-in framework support. Check the ASP.NET Core documentation before relying on any.

**Review checklist**
- [ ] Key pair per client instance; private key never leaves the client or is logged.
- [ ] Fresh `jti` and `iat` per request; `htu` has no query.
- [ ] RS verifies `ath`, key binding to `cnf.jkt`, `jti` replay, nonce.
- [ ] RS rejects DPoP-bound tokens sent as `Bearer`.

---

## Sourcing notes (for maintainers)
- Verbatim, from downloaded text files of rfc-editor.org: RFC 6749 (whole sections 3, 4.1, 5, 6, 10 re-read in step 2), 6750, 7636, 9700, 8693, 7523, 8707, 8414, 9728, 7591, 7662, 7009, 8628, 9449.
- OAuth 2.1: `draft-ietf-oauth-v2-1-17` (31 October 2026, expires 4 May 2027), text from ietf.org archive; sections 1.4 to 5 read verbatim in step 2.
- The ASP.NET Core option names (`UsePkce`, `MapInboundClaims`, `OnChallenge`, `OnForbidden`, `RefreshOnIssuerKeyNotFound`, `ValidTypes`, `ValidAlgorithms`) were confirmed by compiling the samples, not from documentation.

### Corrections made in step 2 after the verbatim re-read
1. Client authentication: step 1 said Basic is mandatory. True for RFC 6749 2.3.1 only; the 2.1 draft (2.4.1) makes the form-body method MUST and Basic MAY. Rule 3 in section 2 rewritten.
2. `redirect_uri` at the token endpoint: step 1 said it must always match. RFC 6749 4.1.3 requires it only when it was in the authorization request; 2.1 drops it. Rule 5 rewritten.
3. Query-string tokens: step 1 mixed RFC 6750 (SHOULD NOT) and 9700 (client MUST NOT); the 2.1 draft adds that the RS MUST ignore them. Section 3 rule 1 rewritten.
4. Added items the summary had missed: `client_id` required from unauthenticated clients (6749 3.2.1); partial redirect registration allowed in 6749 but not 9700/2.1; `code_challenge_method` REQUIRED and `iss` REQUIRED in authorization responses (2.1); CORS rules; https everywhere; 307 excluded; opaque tokens and token length (2.1 1.4, 1.7.1); verifier without challenge is `invalid_request` in 2.1 and rejected per 9700.
5. OIDC Discovery appends to the issuer, RFC 8414 inserts (clarified in section 7).
6. Everything else the fetch-tool summary had said about 6749 sections 3 to 6 and 10 (TLS on authorization and token endpoints, POST only, no fragment, single-use short-lived codes, error codes and 401 rule for `invalid_client`, `no-store`, refresh scope rules, 2^-128 guessing probability, CSRF, open redirectors) matched the verbatim text.
