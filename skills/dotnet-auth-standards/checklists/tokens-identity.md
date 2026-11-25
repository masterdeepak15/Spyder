# Code-review checklist: JWT validation, OIDC and SCIM

For reviewing C# / ASP.NET Core changes. Each item points to the reference section with the rule and example, and to the RFC or specification. Mark an item N/A only if the code does not touch that area. Short rules: see the matching file in `../reference/` (routing in `../SKILL.md`).

Reference files: [T] `../reference/tokens-jwt-details.md`.

## 3. JWT validation

- [ ] Issuer, audience, lifetime, signature and `RequireSignedTokens` explicitly set; none disabled. [T §1, §2](../reference/tokens-jwt-details.md#1-jwt-validation-pitfalls), RFC 7519 7.2, RFC 8725 3
- [ ] `ValidAlgorithms` is an allow-list of what the issuer signs with; no `none`; no HS* verified with a public key. [T §1](../reference/tokens-jwt-details.md#1-jwt-validation-pitfalls), RFC 8725 2.1, 3.1
- [ ] `ValidTypes` requires `at+jwt` for access tokens; ID tokens cannot be used as access tokens. [T §1, §5](../reference/tokens-jwt-details.md#5-rfc-9068-jwt-access-tokens), RFC 9068 2.1, 4
- [ ] Clock skew is a small value set in code (default is 5 min); `exp` required. [T §1](../reference/tokens-jwt-details.md#1-jwt-validation-pitfalls), RFC 7519 4.1.4
- [ ] Required claims are listed in code; claims are read only from the validated token. [T §2](../reference/tokens-jwt-details.md#2-rfc-7519-jwt), RFC 7519 11.1
- [ ] No manual JWT parsing for trust decisions; `jku`, `x5u`, `jwk`, `x5c` from tokens are not followed; `kid` is an opaque lookup key. [T §3](../reference/tokens-jwt-details.md#3-rfc-7515-jws), RFC 7515 4.1, RFC 8725 3.10
- [ ] HMAC keys, if any, are 256 random bits, not passwords; asymmetric preferred. [T §1](../reference/tokens-jwt-details.md#1-jwt-validation-pitfalls), RFC 8725 3.5
- [ ] JWKS comes from trusted metadata over HTTPS; unknown-`kid` refresh is rate-limited; rotation publishes the new key before use and keeps the old one until tokens expire. [T §4](../reference/tokens-jwt-details.md#4-rfc-7517-jwk-and-key-rotation), RFC 7517
- [ ] The JWKS endpoint serves public parameters only (automated test for `d`, `p`, `q`, `k`). [T §4](../reference/tokens-jwt-details.md#4-rfc-7517-jwk-and-key-rotation), RFC 7517 7
- [ ] Tokens carry no more personal data than the API needs. [T §5](../reference/tokens-jwt-details.md#5-rfc-9068-jwt-access-tokens), RFC 9068 6

## 4. Identity (OIDC, SCIM)

- [ ] OIDC authority is the exact issuer; `ResponseType = Code`, `UsePkce = true`; `nonce` generated and checked. [T §6](../reference/tokens-jwt-details.md#6-openid-connect-core), OIDC Core 3.1.3.7
- [ ] User key is `iss` + `sub`, never email or name. [T §6](../reference/tokens-jwt-details.md#6-openid-connect-core), OIDC Core 2
- [ ] UserInfo `sub` equals the ID token `sub` before the response is used. [T §6](../reference/tokens-jwt-details.md#6-openid-connect-core), OIDC Core 5.3.4
- [ ] ID token never sent to APIs; access token never parsed as identity; session lifetime decided independently of ID token `exp`. [T §6](../reference/tokens-jwt-details.md#6-openid-connect-core)
- [ ] Discovery `issuer` equals the URL used to fetch it; `RequireHttpsMetadata` true outside local development. [T §7](../reference/tokens-jwt-details.md#7-openid-connect-discovery), OIDC Discovery 4.3
- [ ] SCIM: TLS only, scopes mapped to read/write/admin, `application/scim+json`, SCIM error schema with string `status`. [T §8](../reference/tokens-jwt-details.md#8-rfc-7644-scim), RFC 7644 3.12, 7
- [ ] SCIM PATCH validated (schema URN, `Operations`, op names, paths); `startIndex`/`count` normalised and capped; filters parsed, never string-built into SQL/LDAP. [T §8](../reference/tokens-jwt-details.md#8-rfc-7644-scim), RFC 7644 3.4.2.4, 3.5.2

## How to report

For each finding give: file and line, the item above, the RFC section, and the fix. Do not claim an item passes without reading the code or running a test for it.
