# Tokens and identity rules: JWT, JWS, JWK, RFC 9068, OIDC, SCIM (C# / ASP.NET Core)

Must-follow rules only. Pitfall table with causes and sample checks, examples, per-section checklists and sourcing notes: [tokens-jwt-details.md](tokens-jwt-details.md) (section numbers match it). Samples: `examples/OAuthSamples/` (`TokenRules.cs`, `Keys.cs`, `IdToken.cs`, `Scim.cs`). OAuth flows: [oauth-oidc.md](oauth-oidc.md).

## 1. Pin everything in JWT validation ([§1](tokens-jwt-details.md#1-jwt-validation-pitfalls))
- `alg: none`: `RequireSignedTokens = true` plus an explicit `ValidAlgorithms` list (RFC 8725 2.1, 3.2).
- Algorithm confusion: `ValidAlgorithms` only asymmetric algorithms you issue (`RS256`, `ES256`); one algorithm per key; no HS* verified with a public key.
- `ValidateIssuer = true`, `ValidIssuer` = the exact AS `issuer` (case-sensitive, trailing slash matters).
- `ValidateAudience = true`, `ValidAudience` = this API. Never disable it to "make it work".
- Access tokens: `ValidTypes` = `at+jwt` / `application/at+jwt`; ID tokens must not pass as access tokens.
- Clock skew: small, set in code (library default is 5 min; sample uses 30 s); NTP on servers.
- `RequireExpirationTime = true`; access tokens one hour or less.
- Keys by `kid` from the issuer JWKS; old keys kept during overlap; unknown `kid` refresh rate-limited (`RefreshOnIssuerKeyNotFound = true` with `Authority`, or `RotatingKeyResolver`).
- Never follow `jku`/`x5u`; do not trust `jwk`/`x5c` in tokens; `kid` is an opaque lookup string (never into SQL/LDAP).
- HMAC only with 256 random bits, never a password; prefer asymmetric.
- Validate first (`ValidateTokenAsync`); read claims only from the validated result (RFC 7519 11.1).
- Summary: pin issuer, audience, algorithm list, `typ` and keys; leave nothing to the token's own header.

## 2. RFC 7519 JWT ([§2](tokens-jwt-details.md#2-rfc-7519-jwt))
- All registered claims are optional in the RFC; your profile lists the required ones (RFC 9068: `iss`, `exp`, `aud`, `sub`, `client_id`, `iat`, `jti`). Enforce the list in code.
- `exp`: now must be before it; `nbf`: now at or after; both NumericDate seconds; small leeway only.
- `aud` string or array; the recipient must find its own identifier or reject. `iss`/`sub`: exact string compare.
- `jti` for replay protection where needed (assertions, DPoP-like), not ordinary access tokens.
- Validation order: structure, header understood, signature, claims; unknown `crit` params invalidate; reject algorithms you do not accept. Nested JWT: sign then encrypt, validate both layers.
- Use `TokenRules.ForAccessTokens(issuer, audience)`; set every switch explicitly.

## 3. RFC 7515 JWS ([§3](tokens-jwt-details.md#3-rfc-7515-jws))
- `alg` present, understood and allow-listed; bind each key to one algorithm; never pick the verifier from the token's `alg`.
- Compact form: exactly two periods, base64url parts without whitespace. Verify with the framework; no manual JWT parsing for trust decisions; no own string compare of signatures.
- `kid` is a hint only; `jku`, `x5u`, `jwk`, `x5c` are attacker-controlled: ignore or restrict to an allow-list of known URLs over TLS.
- `crit`: if an extension is not understood, the JWS is invalid. Use explicit `typ` for new JWT uses.
- ES256 signature is the 64-octet `R||S` (do not convert to DER).

## 4. RFC 7517 JWK and rotation ([§4](tokens-jwt-details.md#4-rfc-7517-jwk-and-key-rotation))
- JWK needs `kty`; `use` and `key_ops` must not disagree; JWK Set `{"keys":[...]}`; ignore keys with unknown `kty` or bad members; distinct `kid` per key.
- Publish public parameters only; automated test that output has no `d`, `p`, `q`, `dp`, `dq`, `qi`, `k` (`Jwks.ContainsPrivateMaterial`).
- Rotation: publish the new key before signing with it; keep the old until all its tokens expire; unique `kid`.
- Cache JWKS; refresh on unknown `kid`, rate-limited; fetch only over HTTPS from the metadata `jwks_uri` (never from a token).
- Separate signing and encryption keys; only `sign`/`verify`, `encrypt`/`decrypt`, `wrapKey`/`unwrapKey` pairs.

## 5. RFC 9068 access tokens ([§5](tokens-jwt-details.md#5-rfc-9068-jwt-access-tokens))
- AS: asymmetric signature (RS256 must be supported), never `none`, header `typ` `at+jwt`.
- Required claims: `iss`, `exp`, `aud`, `sub`, `client_id`, `iat`, `jti`. `scope` is a space-delimited string.
- `aud` = resource indicator (RFC 8707), distinct per resource; with no `resource` the AS uses a default resource indicator.
- RS must check: `typ`, `iss` exact, `aud` contains own identifier, signature with the AS keys and declared `alg` (not `none`), `exp` in the future. Any failure = 401 `error="invalid_token"`; scope failure = 403 `insufficient_scope`.
- Scope/roles/groups: split the string into a set, never substring search (`orders:read` vs `orders:readonly`); named policies; combine with local policy.
- Client-credentials `sub` identifies the client; AS prevents `client_id` values that collide with user subjects.
- A leaked key in the published set lets an attacker mint tokens; separate ID/access keys do not protect the RS.
- No more personal data in tokens than the RS needs; allow header sizes of 1 KB and above.

## 6. OpenID Connect Core ([§6](tokens-jwt-details.md#6-openid-connect-core))
- Code flow + PKCE, scope `openid`; Implicit/Hybrid not covered. `AddOpenIdConnect`: `ResponseType = Code`, `UsePkce = true`, `SaveTokens = true`, `MapInboundClaims = false`; extra checks (`acr`, `auth_time`, `max_age`) in `OnTokenValidated`.
- ID token required claims: `iss`, `sub`, `aud`, `exp`, `iat`. User key is `iss` + `sub`, never email or name.
- Validate: `iss` equals the discovered issuer; `aud` contains your `client_id` and no untrusted extras; signature with expected `alg` (RS256 default; avoid HS256); `exp`; same `nonce` you sent; `auth_time`/`acr` if requested.
- `nonce`: high entropy per request, bound to the user agent, compared on return (PKCE, nonce or state needed per RFC 9700).
- ID token is never an API credential; an access token is never parsed as identity.
- UserInfo: `sub` must equal the ID token `sub` or the response is not used.
- Session lifetime is decided independently of ID token `exp`.

## 7. OIDC Discovery ([§7](tokens-jwt-details.md#7-openid-connect-discovery))
- Configure only the issuer; fetch `{issuer}/.well-known/openid-configuration` (append; RFC 8414 inserts). Use `Authority` and its `ConfigurationManager`; no custom fetcher.
- Returned `issuer` must be identical to the URL issuer and to ID token `iss`; exact, no normalisation. On failure do not use the document.
- `jwks_uri` https; `RequireHttpsMetadata` true outside local development. Values consistent with RFC 8414 metadata if both are published.

## 8. SCIM RFC 7644 ([§8](tokens-jwt-details.md#8-rfc-7644-scim))
- TLS only (1.2+); authenticate every request (OAuth bearer); map scopes to read/write/admin; anonymous only for deliberate self-registration.
- Media type `application/scim+json` (also accept `application/json`); `PUT` replaces and never creates; `POST` creates.
- Errors: schema `urn:ietf:params:scim:api:messages:2.0:Error`, `status` as a string, `scimType` where defined; 409 duplicates/conflicts, 412 failed precondition.
- PATCH: schema `urn:ietf:params:scim:api:messages:2.0:PatchOp`, non-empty `Operations`, one `op` of add/remove/replace, `path` required for remove, no `move`; 501 if unsupported.
- Paging: `startIndex` 1-based (below 1 = 1), negative `count` = 0, cap `count`, return `totalResults`, `itemsPerPage`, `startIndex`.
- Filters: parse with a grammar, never string-build SQL/LDAP; sensitive filters via `POST .../.search`, server may answer 403 `sensitive`.

## Review
Per-area checklist: [../checklists/tokens-identity.md](../checklists/tokens-identity.md).
