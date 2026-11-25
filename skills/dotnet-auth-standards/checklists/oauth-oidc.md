# Code-review checklist: OAuth 2.0 / 2.1 / OIDC flows and bearer tokens

For reviewing C# / ASP.NET Core changes. Each item points to the reference section with the rule and example, and to the RFC or specification. Mark an item N/A only if the code does not touch that area. Short rules: see the matching file in `../reference/` (routing in `../SKILL.md`).

Reference files: [O] `../reference/oauth-oidc-details.md`, [T] `../reference/tokens-jwt-details.md`.

## 1. OAuth 2.0 / 2.1 / OIDC flows

- [ ] Authorization code + PKCE `S256` for user flows; no implicit, no password grant. [O §1, §4, §5](../reference/oauth-oidc-details.md#5-rfc-9700-and-oauth-21), RFC 7636, RFC 9700
- [ ] Redirect URIs are exact strings compared ordinally; no wildcards. [O §5](../reference/oauth-oidc-details.md#5-rfc-9700-and-oauth-21), RFC 9700
- [ ] PKCE verifier from a CSPRNG, 43+ chars, fresh per request. [O §4](../reference/oauth-oidc-details.md#4-rfc-7636-pkce), RFC 7636
- [ ] No secrets in source or committed appsettings; public clients have no secret. [O §2](../reference/oauth-oidc-details.md#2-rfc-6749), RFC 6749 2.3
- [ ] Token endpoint errors are mapped from the `error` code, not message text; old refresh token replaced when a new one is issued. [O §2](../reference/oauth-oidc-details.md#2-rfc-6749), RFC 6749 5.2
- [ ] Public-client refresh tokens are rotated or DPoP-bound; logout calls revocation. [O §5, §10](../reference/oauth-oidc-details.md#10-rfc-7662-and-7009), RFC 9700, RFC 7009
- [ ] Downstream calls use client credentials or an exchanged token with the downstream `audience`/`resource`; the inbound token is never forwarded. [O §6, §9](../reference/oauth-oidc-details.md#9-rfc-8693-and-7523), RFC 8707, RFC 8693
- [ ] Discovery is used instead of hard-coded endpoints; well-known URL built by path insertion; `issuer`/`resource` compared for exact equality. [O §7](../reference/oauth-oidc-details.md#7-rfc-8414-and-9728), RFC 8414, RFC 9728
- [ ] Introspection callers check `active`, `aud` and `scope`; cache bounded by `exp`. [O §10](../reference/oauth-oidc-details.md#10-rfc-7662-and-7009), RFC 7662
- [ ] Device flow: interval honoured, `slow_down` adds 5 s, `user_code` brute force limited. [O §11](../reference/oauth-oidc-details.md#11-rfc-8628), RFC 8628
- [ ] DPoP: fresh `jti`/`iat`, RS verifies `ath`, `cnf.jkt` and replay; DPoP-bound tokens rejected as `Bearer`. [O §12](../reference/oauth-oidc-details.md#12-rfc-9449-dpop), RFC 9449
- [ ] Anything attributed to "OAuth 2.1" is labelled as an Internet-Draft; binding rules cite RFC 9700. [O §5](../reference/oauth-oidc-details.md#5-rfc-9700-and-oauth-21)

## 2. Bearer tokens on the API

- [ ] Token only in the `Authorization` header; no `access_token` query parameter; never logged. [O §3](../reference/oauth-oidc-details.md#3-rfc-6750), RFC 6750
- [ ] 401 with `WWW-Authenticate: Bearer` (and `error="invalid_token"` only when a token was sent and failed); 403 `insufficient_scope` for scope failures; `resource_metadata` advertised. [O §3, §7](../reference/oauth-oidc-details.md#3-rfc-6750), RFC 6750, RFC 9728
- [ ] Validation exception text is not echoed to the caller. [O §3](../reference/oauth-oidc-details.md#3-rfc-6750)
- [ ] `ValidateAudience = true`, `ValidAudience` is this API; tested with a token for another audience. [O §6](../reference/oauth-oidc-details.md#6-rfc-8707), RFC 8707
- [ ] Scope/role checks via a splitting helper and named policies, never `Contains` on the raw scope string. [T §5](../reference/tokens-jwt-details.md#5-rfc-9068-jwt-access-tokens), RFC 9068 2.2.3

## How to report

For each finding give: file and line, the item above, the RFC section, and the fix. Do not claim an item passes without reading the code or running a test for it.
