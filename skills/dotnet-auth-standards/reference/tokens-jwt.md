# Tokens and identity guidelines for C# / ASP.NET Core

Scope: JWT (RFC 7519), JWS (RFC 7515), JWK (RFC 7517), JWT access tokens (RFC 9068), OpenID Connect Core and Discovery, SCIM (RFC 7644). The JWT best-practice RFC 8725 is used for the pitfalls section (it is not in the original list but is the standard source for them).

Samples: `examples/OAuthSamples/` (`TokenRules.cs`, `Keys.cs`, `IdToken.cs`, `Scim.cs`); every claim below marked "tested" is a check in `dotnet run -- selftest`. The OAuth side (flows, PKCE, DPoP) is in `oauth-oidc.md`.

## Contents
1. [JWT validation pitfalls (read first)](#1-jwt-validation-pitfalls)
2. [RFC 7519 JWT](#2-rfc-7519-jwt)
3. [RFC 7515 JWS](#3-rfc-7515-jws)
4. [RFC 7517 JWK and key rotation](#4-rfc-7517-jwk-and-key-rotation)
5. [RFC 9068 JWT access tokens](#5-rfc-9068-jwt-access-tokens)
6. [OpenID Connect Core](#6-openid-connect-core)
7. [OpenID Connect Discovery](#7-openid-connect-discovery)
8. [RFC 7644 SCIM](#8-rfc-7644-scim)
9. [Sourcing notes](#sourcing-notes)

---

## 1. JWT validation pitfalls

| Pitfall | What goes wrong | Rule (source) | Sample check |
|---|---|---|---|
| `alg: none` | An attacker strips the signature and sets `alg` to `none`; a careless validator accepts it. RFC 7519 requires libraries to implement `none`, so acceptance must be an application decision. | Set `RequireSignedTokens = true` and an explicit `ValidAlgorithms` list; never consume `none` unless the caller asked for it (RFC 7519 7.2 end, RFC 8725 2.1, 3.2; RFC 9068 4 says the RS MUST reject `none`). | tested: rejected; a control with deliberately loose parameters accepts the same token, so the test token is well-formed |
| Algorithm confusion | A token declared `HS256` is verified with the RSA public key bytes used as the HMAC secret (RFC 8725 2.1). | Restrict `ValidAlgorithms` to the asymmetric algorithms you issue (`RS256`, `ES256`); each key is used with exactly one algorithm (RFC 8725 3.1). | tested: HS256 signed with the RSA public key bytes is rejected, also when the verifier key is that secret |
| Issuer | Token from another issuer, or a look-alike value. | `ValidateIssuer = true`, `ValidIssuer` exactly equal to the AS `issuer` (case-sensitive string compare, trailing slash matters). The keys used must belong to that issuer (RFC 8725 3.8, RFC 9068 4). | tested: other issuer and trailing-slash issuer rejected |
| Audience | A token meant for API A is replayed at API B (substitution, RFC 8725 2.7). | `ValidateAudience = true`, `ValidAudience` = this API; the token must contain the recipient's identifier or be rejected (RFC 7519 4.1.3, RFC 8725 3.9). Never turn audience validation off to "make it work". | tested |
| Token type | An ID token is presented as an access token (cross-JWT confusion, RFC 8725 2.8, 3.11, 3.12). | Access tokens: require `typ` `at+jwt` (or `application/at+jwt`) via `ValidTypes` (RFC 9068 2.1, 4). Use a distinct `aud`. | tested: `typ: JWT` rejected, `application/at+jwt` accepted |
| Clock skew | Tokens rejected between servers with drifting clocks, or skew set so large that expired tokens live on. | Small leeway only, "usually no more than a few minutes" (RFC 7519 4.1.4). The library default is 5 minutes (tested); the sample sets 30 seconds. Keep servers on NTP. | tested: expired 10 s ago accepted, 120 s ago rejected (30 s skew) |
| Expiry | Tokens without `exp` live forever. | `RequireExpirationTime = true`; keep access tokens short (an hour or less is the RFC 6750 recommendation). | in `TokenRules` |
| Key rotation | New `kid` appears at the AS and every request fails, or every request triggers a JWKS download. | Look up keys by `kid`; keep old keys during overlap; on an unknown `kid` refresh the key set at most once per interval. With `Authority`, `RefreshOnIssuerKeyNotFound = true` does this for you; for self-validation use `RotatingKeyResolver`. | tested: miss, no refetch inside the interval, refetch after it, old key still valid |
| Untrusted header URLs and `kid` | `jku`/`x5u` headers pointing at attacker URLs (SSRF) and `kid` injected into a database or LDAP lookup (RFC 8725 3.10). A `jwk`/`x5c` header carries a key chosen by the token's sender (RFC 7515 4.1.3). | Take keys only from the issuer's configured JWKS; do not follow `jku`/`x5u` or trust `jwk`/`x5c` in tokens (guidance derived from the cited text); treat `kid` as an opaque lookup string. | by design: the resolver only looks up configured keys |
| Weak HMAC key | A password used as an HS256 key is brute-forced offline (RFC 8725 2.2, 3.5). | Prefer asymmetric signing. If HS256 is unavoidable, use 256 random bits and never a human password. | n/a |
| Trusting claims before verification | Reading `sub` or `role` from an unverified token. | Validate first (`ValidateTokenAsync`), read claims from the validated result only (RFC 7519 11.1). | tested: payload tampering rejected |

**The one-line summary:** pin issuer, audience, algorithm list, `typ`, and signing keys; leave nothing to the token's own header.

---

## 2. RFC 7519 JWT

**Rules**
1. A JWT is a claims set in a JWS or JWE. Registered claims (`iss`, `sub`, `aud`, `exp`, `nbf`, `iat`, `jti`) are all OPTIONAL in RFC 7519; your profile decides which are required (RFC 9068 requires `iss`, `exp`, `aud`, `sub`, `client_id`, `iat`, `jti` for access tokens). Define and enforce that list.
2. `exp`: the current time MUST be before it. `nbf`: the current time must be after or equal. Both are NumericDate (seconds since epoch as a JSON number). Allow only a small leeway (RFC 7519 4.1.4, 4.1.5).
3. `aud`: a string or array of strings; every recipient must identify itself with a value and MUST reject the JWT if it does not appear (4.1.3).
4. `sub` must be unique within the issuer, or globally; `iss` is a case-sensitive StringOrURI. Compare these as exact strings (7.3).
5. `jti` is a unique ID; use it for replay protection where needed (assertions, DPoP-like patterns), not for ordinary access tokens.
6. Validation (7.2) includes: structure, header understood, signature, then claims. Unknown critical header parameters invalidate the token. Even when validation succeeds, the application should reject algorithms it does not accept (7.2 end).
7. Nested JWTs: sign, then encrypt (11.2); validate both layers (RFC 8725 3.3).

**Example** (`TokenRules.cs`, tested):

```csharp
public static TokenValidationParameters ForAccessTokens(string issuer, string audience) => new()
{
    ValidateIssuer = true, ValidIssuer = issuer,
    ValidateAudience = true, ValidAudience = audience,
    ValidateLifetime = true, RequireExpirationTime = true, ClockSkew = TimeSpan.FromSeconds(30),
    RequireSignedTokens = true, ValidateIssuerSigningKey = true,
    ValidAlgorithms = [SecurityAlgorithms.RsaSha256, SecurityAlgorithms.EcdsaSha256],
    ValidTypes = ["at+jwt", "application/at+jwt"]
};
```

**Review checklist**
- [ ] Every validation switch above is explicitly set (no reliance on a lowered default, none turned off).
- [ ] `ValidAlgorithms` lists only what the issuer signs with.
- [ ] Claims are read from the validated token only.
- [ ] Required claims are listed in code, not assumed.

---

## 3. RFC 7515 JWS

**Rules**
1. `alg` MUST be present and understood; the signature is invalid if `alg` is unsupported or no key is associated with it (4.1.1). In practice: allow-list `alg`, and bind each key to one algorithm (RFC 8725 3.1).
2. Compact serialization is `base64url(header).base64url(payload).base64url(signature)` with exactly two periods; the signing input is `ASCII(b64(header) || '.' || b64(payload))` (5.2). No whitespace or line breaks in the base64url parts.
3. `kid` is a hint to pick a key (4.1.4); `jku`/`x5u` (a URL of keys), `jwk` and `x5c` (keys in the token) are attacker-controlled input. Ignore them, or restrict to an allow-list of known URLs with TLS (4.1.2; RFC 8725 3.10).
4. `crit` lists extensions that MUST be understood; if one is not, the JWS is invalid (4.1.11). Do not ignore it.
5. `typ` can mark the kind of JWT (4.1.9); use explicit typing for new JWT uses (RFC 8725 3.11).
6. Verify the algorithm information inside the signature value too, for RSASSA-PKCS1-v1_5 (10.6); avoid timing differences in comparisons (10.9). Use the framework's verify, never your own string compare of signatures.
7. ES256 signatures are the 64-octet `R||S` concatenation, each 32 octets and not shortened (RFC 7518 3.4, read verbatim). `ECDsa.SignData` with its default format produced a 64-octet signature in the selftest; do not convert to DER.

**Example**: signing and validating are done by `JsonWebTokenHandler` in the tests (`MakeToken` creates tokens with a chosen `typ`, `alg` and key; `Accepts` validates with `TokenRules`).

**Review checklist**
- [ ] No manual JWT parsing for trust decisions; the handler validates.
- [ ] `jku`, `x5u`, `jwk`, `x5c` headers from tokens are not followed.
- [ ] Algorithm allow-list in code; no `alg` read from the token to choose the verifier.

---

## 4. RFC 7517 JWK and key rotation

**Rules**
1. A JWK needs `kty`; common members are `use` (`sig` or `enc`), `key_ops`, `alg`, `kid`. `use` and `key_ops` SHOULD NOT both appear, and must agree if they do (4.2, 4.3). Member names must be unique; unknown members are ignored (4).
2. A JWK Set is `{"keys":[...]}`. Consumers should ignore keys with unknown `kty`, missing required members or out-of-range values (5). Within a set, different keys SHOULD have distinct `kid` values (4.5).
3. Publish public parameters only. A JWK containing private or symmetric material must never be served; protect it by encryption if it must travel (7). Test your endpoint output for `d`, `p`, `q`, `dp`, `dq`, `qi`, `k`.
4. Rotation: publish the new key before signing with it; keep the old key in the set until all tokens signed with it have expired; then remove it. Give every key a unique `kid`.
5. Cache the JWKS with a sensible lifetime and refresh on an unknown `kid` (rate-limited). Fetch only over HTTPS from the metadata `jwks_uri` (RFC 8414 / OIDC Discovery require https for `jwks_uri`).
6. Do not give one key unrelated operations: only `sign` with `verify`, `encrypt` with `decrypt`, and `wrapKey` with `unwrapKey` are acceptable combinations (4.3). Keep signing keys separate from encryption keys.

**Example** (`Keys.cs`, tested):

```csharp
var resolver = new RotatingKeyResolver(() => FetchJwks(), TimeSpan.FromMinutes(5));
parameters.IssuerSigningKeyResolver = resolver.Resolve;    // kid lookup, refresh at most every 5 minutes on a miss
```

`Jwks.PublicJwksJson` writes `kty`, `use`, `alg`, `kid`, `n`, `e` only; `Jwks.SigningKeys` reads a set; `Jwks.ContainsPrivateMaterial` is the leak check.

**Review checklist**
- [ ] The JWKS endpoint serves public parameters only (automated test).
- [ ] New key published before first use; old key kept through the overlap.
- [ ] Unknown `kid` refresh is rate-limited; no refetch per request.
- [ ] `jwks_uri` comes from trusted metadata, not from a token.

---

## 5. RFC 9068 JWT access tokens

**Rules**
1. The AS signs the token (asymmetric recommended), never uses `none`, and sets header `typ` to `at+jwt` (the `application/at+jwt` form is also valid). RS and AS must support RS256 (2.1).
2. Required claims: `iss`, `exp`, `aud`, `sub`, `client_id`, `iat`, `jti` (2.2). Optional: `auth_time`, `acr`, `amr`, `scope` (a space-delimited string, per RFC 8693 4.2), and `groups`, `roles`, `entitlements` in the SCIM RFC 7643 shape (2.2.3.1).
3. `aud` is the resource indicator: when the request carries `resource` (RFC 8707), `aud` SHOULD equal it; with no `resource`, the AS must use a default resource indicator (3). Use a distinct `aud` per resource to prevent cross-JWT confusion (5).
4. RS validation (4), all required: `typ` is `at+jwt` or `application/at+jwt`; `iss` exactly equals the expected issuer; `aud` contains this RS's identifier; signature valid with the AS's keys and the declared algorithm, `alg` not `none`; `exp` in the future (small leeway). On any failure respond with `error="invalid_token"` (RFC 6750 3.1).
5. Authorization claims (`scope`, `roles`, `groups`) are combined with local policy; the RFC does not tell you how. Treat `scope` as a space-delimited set: split it, do not substring-search (tested: `orders:read` does not match `orders:readonly`).
6. `sub` for client-credentials tokens identifies the client; the AS must stop clients choosing arbitrary `client_id` values that collide with user subjects (5).
7. A leaked signing key anywhere in the published set lets an attacker mint tokens: using separate keys for ID tokens and access tokens does not protect the RS, because it must accept all published keys (5).
8. Privacy: do not put more personal data into access tokens than the RS needs (6).

**Example** (`ResourceServer.AddApiAuth`): sets `o.TokenValidationParameters = TokenRules.ForAccessTokens(issuer, audience)` and `RefreshOnIssuerKeyNotFound = true`; the live request test returned 401 with `Bearer error="invalid_token", resource_metadata=...` for a bad token.

**Review checklist**
- [ ] `ValidTypes` set to `at+jwt` forms; ID tokens cannot be used as access tokens.
- [ ] `aud` is this API's own identifier; not shared with other APIs of the same issuer.
- [ ] `scope`/roles read through a splitting helper; policies named after scopes.
- [ ] 401 `invalid_token` on every validation failure, 403 `insufficient_scope` for scope failures.
- [ ] Tokens kept small enough for header limits, but no artificial RS limit below ~1 KB (OAuth 2.1 draft 1.7.1).

---

## 6. OpenID Connect Core

**Rules**
1. OIDC adds authentication on top of OAuth: request scope `openid`, receive an ID token (a JWT) at the token endpoint (code flow). Use the authorization code flow with PKCE; the Implicit and Hybrid flows are not covered by this skill (OAuth 2.1 and RFC 9700 steer away from tokens in the front channel).
2. ID token claims: `iss`, `sub`, `aud`, `exp`, `iat` REQUIRED; `auth_time` (REQUIRED when `max_age` was requested), `nonce`, `acr`, `amr`, `azp` optional. `sub` is at most 255 ASCII characters and never reassigned. Use `iss` + `sub` as the user key, never email or name.
3. Validation (Core 3.1.3.7), tested in `IdTokenValidator`: `iss` exactly equals the issuer from discovery; `aud` contains your `client_id`, and the token is rejected if it lists other audiences you do not trust; signature verified with the issuer's keys and an expected `alg` (default RS256); `exp` in the future; if you sent a `nonce`, the token MUST carry the same value; check `auth_time` if you required freshness; check `acr` if you requested it. TLS server validation MAY replace the signature check only for tokens received directly from the token endpoint; do not rely on that for tokens from anywhere else.
4. `nonce`: send a high-entropy value per request, bind it to the user agent (a cookie), compare on return. In the code flow it is optional per Core but cheap and recommended; PKCE is the other CSRF/injection defence (RFC 9700 requires one of PKCE or nonce or state).
5. Do not use the ID token as an API credential; use the access token (and RFC 9068 `typ` stops confusion at the RS). Do not accept an access token as an ID token.
6. UserInfo: call with the access token; the `sub` in the response MUST exactly match the ID token's `sub`, or the response must not be used (5.3.4, tested). Validate the OP by TLS certificate; validate the signature if the response is signed.
7. HMAC-signed ID tokens (HS256) use the client secret as the key; avoid, prefer RS256/ES256 (Core 3.1.3.7).
8. In ASP.NET Core: `AddOpenIdConnect` validates issuer, audience, signature, lifetime and nonce for you; keep `ResponseType = Code`, `UsePkce = true`, `SaveTokens = true`, `MapInboundClaims = false` (see `OidcClient.cs` in step 1). Add your own checks (`acr`, `auth_time`, `max_age`) in `OnTokenValidated`.

**Example** (`IdToken.cs`, tested): `IdTokenValidator.ValidateAsync(idToken, issuer, clientId, nonce, keys)` returns `(Ok, Error)`; tests cover valid token, wrong nonce, wrong audience, extra audience, wrong issuer, and the UserInfo `sub` match.

**Review checklist**
- [ ] Authority is the exact issuer; discovery over HTTPS.
- [ ] Code flow + PKCE; `nonce` generated and checked.
- [ ] User identity key is `iss` + `sub`.
- [ ] UserInfo `sub` compared with the ID token `sub`.
- [ ] No ID token sent to APIs; no access token parsed as identity.
- [ ] Session/cookie lifetime decided independently of ID token `exp` (Core 2 NOTE: ID token expiry is unrelated to the session).

---

## 7. OpenID Connect Discovery

**Rules**
1. Fetch `GET {issuer}/.well-known/openid-configuration`, appending to the issuer; with a path component remove a trailing `/` first (4.1). This differs from RFC 8414, which inserts the well-known segment after the host (see `oauth-oidc.md` section 7). If you support both, try the one the issuer advertises; `Metadata.OidcConfiguration` and `Metadata.WellKnown` build the two forms (both tested against the spec examples).
2. Required provider metadata includes `issuer`, `jwks_uri`, `subject_types_supported`, `id_token_signing_alg_values_supported` (plus authorization, token endpoints and response types). `jwks_uri` MUST be https. In `token_endpoint_auth_signing_alg_values_supported` the value `none` MUST NOT be used and servers SHOULD support RS256.
3. Validate (4.3): the `issuer` returned MUST be identical to the issuer URL used for retrieval, and also identical to the `iss` of ID tokens. On any validation failure, do not use the information.
4. The configuration endpoint SHOULD support CORS for browser clients; serve `application/json`.
5. Cache the document and refresh on a schedule; rely on `Authority` (the `ConfigurationManager` does this) rather than writing your own fetcher.
6. If an AS publishes both OIDC discovery and RFC 8414 metadata, their values MUST be consistent (RFC 9068 4).

**Review checklist**
- [ ] Only the issuer is configured; endpoints and keys come from discovery.
- [ ] Issuer comparison is exact (no normalisation, no trailing-slash tolerance).
- [ ] `RequireHttpsMetadata` stays true outside local development.

---

## 8. RFC 7644 SCIM

SCIM is a REST protocol for provisioning users and groups (schemas are in RFC 7643, not covered here).

**Rules**
1. Authenticate every request; SCIM transports passwords and personal data, so TLS is mandatory (7.2: clients and service providers MUST require TLS; providers MUST support TLS 1.2). Bearer tokens (OAuth) are common; map scopes or roles to access control (2.1) and use tokens with enough entropy and a limited lifetime (7.4). Anonymous requests are only for deliberate cases such as self-registration (2.2, 7.6).
2. Endpoints: `/Users`, `/Groups`, `/Me`, `/ServiceProviderConfig`, `/ResourceTypes`, `/Schemas`, `/Bulk`, and `[prefix]/.search`. Discover custom resource endpoints through `/ResourceTypes` (3.2).
3. Media type: `application/scim+json` is the default and must be supported on `Accept`; also accept `application/json` (3.8). `PUT` replaces and MUST NOT create; `POST` creates (3.2).
4. Errors (3.12): JSON body with `schemas` `["urn:ietf:params:scim:api:messages:2.0:Error"]`, `status` as a JSON string, optional `scimType` and `detail`. `scimType` values include `invalidFilter`, `tooMany`, `uniqueness`, `mutability`, `invalidSyntax`, `invalidPath`, `noTarget`, `invalidValue`, `invalidVers`, `sensitive`. Use 409 for duplicates or version conflicts, 412 for failed preconditions.
5. PATCH (3.5.2): body has `schemas` with `urn:ietf:params:scim:api:messages:2.0:PatchOp` and an `Operations` array; each operation has exactly one `op` of `add`, `remove`, `replace`; `path` is optional for add/replace and required for remove. It is based on JSON Patch (RFC 6902) but has no array indexing and no `move`. PATCH is optional: return 501 if unsupported.
6. Paging (3.4.2.4): `startIndex` is 1-based (a value below 1 is treated as 1); `count` negative is treated as 0; the server must not return more than `count` and may return fewer; responses carry `totalResults`, `itemsPerPage`, `startIndex`. Paging is not stateful, so clients must tolerate inconsistent results.
7. Sensitive filters: filters in GET query strings can leak personal data into URLs and logs; clients SHOULD use `POST .../.search`, and servers MAY answer `403` with `scimType` `sensitive` (7.5.2).
8. Do not trust `id` values or filters from the client for injection-prone back ends; parse filters with a grammar, never string-concatenate them into SQL/LDAP.

**Example** (`Scim.cs`, tested): `Scim.ErrorBody`, `Scim.Error` (returns `application/scim+json` with the right status), `Scim.ValidatePatch` and `Scim.NormalizePaging`. One caveat recorded in the code: the RFC text read for this skill lists the `op` values in lower case but does not say whether case matters, so the sample accepts any case.

**Review checklist**
- [ ] Content type `application/scim+json` on SCIM responses; `Accept` negotiated.
- [ ] Errors use the SCIM error schema with string `status` and a `scimType` where defined.
- [ ] PATCH validates schema URN, non-empty `Operations`, op names, and path rules.
- [ ] `startIndex`/`count` normalised and capped; `totalResults` returned.
- [ ] Filters parsed by a real parser; no string-built queries.
- [ ] Token scopes map to read, write and admin SCIM operations.

---

## Sourcing notes
- Read verbatim (downloaded text, then read): RFC 7519 (4.1, 7, 8, 11), RFC 7515 (4.1.1 to 4.1.11 headers, 5.2, 10.3 to 10.9), RFC 7517 (4, 5, 7), RFC 9068 (2 to 5), RFC 8725 (2, 3), OpenID Connect Core (ID token claims, 3.1.3.7 validation, 5.3.4), OpenID Connect Discovery (3, 4.1, 4.3), RFC 7644 (2, 3.2, 3.4.2.4, 3.5.2, 3.8, 3.12, 7).
- Not read in full, so not summarised here: SCIM filtering grammar (3.4.2.2), bulk (3.7), versioning/ETags (3.14), service provider configuration (4), multi-tenancy (6); Core sections on Implicit/Hybrid flows, request objects, claims request parameter, self-issued OP; JWE (RFC 7516) and JWA (RFC 7518) algorithms.
- Library facts (`ValidTypes`, `ValidAlgorithms`, `RequireSignedTokens`, `RefreshOnIssuerKeyNotFound`, `JsonWebKeySet`, `JsonWebTokenHandler`) come from compiling and running the samples against Microsoft.IdentityModel 8.19.2 (pulled in by the 10.0.15 ASP.NET Core auth packages), not from documentation. The library's default `ClockSkew` of five minutes is a selftest check. RFC 7518 3.4 (ES256 format) was read verbatim for the signature-size rule.
