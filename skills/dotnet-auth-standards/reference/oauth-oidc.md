# OAuth 2.0 / 2.1 rules (C# / ASP.NET Core)

Must-follow rules only. Explanations, examples and per-section checklists: [oauth-oidc-details.md](oauth-oidc-details.md) (section numbers below match it). Samples: `examples/OAuthSamples/`. OIDC, JWT, JWKS, SCIM rules: [tokens-jwt.md](tokens-jwt.md). OAuth 2.1 is an Internet-Draft: cite RFC 9700 for binding rules.

## 1. Grants ([§1](oauth-oidc-details.md#1-baseline))
- User present: authorization code + PKCE S256. Never implicit, never password (ROPC).
- Service to service: client credentials. Device: RFC 8628. Service acting for a user: token exchange RFC 8693. Never forward the inbound token.

## 2. RFC 6749 ([§2](oauth-oidc-details.md#2-rfc-6749))
- API validates tokens; it does not issue them unless it is the AS.
- Public clients (SPA, native, device) hold no `client_secret`; `client_id` alone never authenticates. Prefer `private_key_jwt` or mTLS. 6749 makes Basic mandatory; the 2.1 draft makes form-body mandatory. With Basic, form-urlencode id and secret first.
- Token requests: POST, `application/x-www-form-urlencoded`, TLS. Token responses: `Cache-Control: no-store`.
- Codes are single use, 10 minutes at most; reuse is denied and its tokens revoked. Unauthenticated clients send `client_id` at the token endpoint. Client credentials is for confidential clients only.
- Never redirect to an unregistered or mismatching URI. Map errors from the `error` code (`invalid_grant`, `invalid_client`, `invalid_scope`, ...), not message text.
- Refresh: scope never exceeds the original grant; when a new refresh token is issued, discard the old one.

## 3. RFC 6750 ([§3](oauth-oidc-details.md#3-rfc-6750))
- Token only in `Authorization: Bearer`. Never in query strings (RS ignores them, 2.1), URLs or logs. Tokens are opaque to clients; do not cap length (JWTs exceed 1 KB; check Kestrel/proxy header limits).
- Challenge: no token = 401 `WWW-Authenticate: Bearer` without `error`; bad token = 401 `error="invalid_token"`; too little scope = 403 `insufficient_scope`; malformed = 400 `invalid_request`. No repeated attributes. Do not echo validation exception text.
- Short lifetime (1 hour or less), audience-restricted, TLS only.

## 4. RFC 7636 PKCE ([§4](oauth-oidc-details.md#4-rfc-7636-pkce))
- PKCE for all code flows. `S256` only, send `code_challenge_method` explicitly (default is `plain`).
- Verifier: 32 bytes from `RandomNumberGenerator`, base64url (43 chars); fresh per request; never `Random` or timestamps.
- AS: bind challenge to the code, require `code_verifier`, mismatch = `invalid_grant`, reject verifier without challenge (downgrade); publish `code_challenge_methods_supported`.
- `AddOpenIdConnect`: `ResponseType = Code`, `UsePkce = true` set explicitly.

## 5. RFC 9700 / OAuth 2.1 ([§5](oauth-oidc-details.md#5-rfc-9700-and-oauth-21))
- Redirect URIs: full URI, exact `StringComparison.Ordinal` match; no wildcard, prefix or open redirector; no `http` except loopback (port may vary). All OAuth URLs https.
- CSRF/injection: PKCE, or OIDC `nonce`, or one-time `state` bound to the user agent (protect data in `state`).
- Mix-up: with several AS, store the expected issuer per request and compare it with `iss` (RFC 9207) or the ID token `iss`; abort on mismatch.
- Sender-constrain (mTLS or DPoP) and audience-restrict tokens. Public-client refresh tokens: rotate (one-time) or sender-constrain; expire on inactivity; revoke on security events.
- Use 303, not 307, for redirects carrying credentials. Authorization endpoint: no CORS; token/metadata/registration/revocation/introspection endpoints send CORS for browser apps; browser app needing a secret uses a BFF.

## 6. RFC 8707 ([§6](oauth-oidc-details.md#6-rfc-8707))
- Send `resource=<absolute API URI>` (no fragment) on authorization and token requests; one per request; handle `invalid_target`.
- RS: `ValidateAudience = true`, `ValidAudience` = this API; reject other audiences even with a valid signature; test with a foreign-audience token. Multi-tenant: tenant in the resource URI.

## 7. RFC 8414 / 9728 ([§7](oauth-oidc-details.md#7-rfc-8414-and-9728))
- RFC 8414 URL: path INSERTED (`/.well-known/oauth-authorization-server/issuer1`). OIDC Discovery APPENDS (`/issuer1/.well-known/openid-configuration`). Use `Metadata.WellKnown`.
- Metadata `issuer` and protected-resource `resource` must exactly equal the identifier used to build the URL, else discard. Never normalise.
- Serve `/.well-known/oauth-protected-resource` (`resource`, `authorization_servers`, `scopes_supported`); put `resource_metadata="..."` in the 401 challenge.
- Do not hard-code endpoints; discover, fetch over HTTPS, cache with TTL, ignore unknown members.

## 8. RFC 7591 ([§8](oauth-oidc-details.md#8-rfc-7591))
- Registration: POST JSON over TLS, 201 with `client_id`; errors `invalid_redirect_uri`, `invalid_client_metadata`, `invalid_software_statement`.
- Public clients: `token_endpoint_auth_method: "none"`. Redirects: https, loopback http, or app scheme only. `jwks` and `jwks_uri` never together; grant/response types consistent. Secrets unique, `client_secret_expires_at` required, stored hashed, shown once.
- Open registration: rate-limit and validate every field (metadata is self-asserted).

## 9. RFC 8693 / 7523 ([§9](oauth-oidc-details.md#9-rfc-8693-and-7523))
- Exchange: `grant_type=urn:ietf:params:oauth:grant-type:token-exchange` with `subject_token` + `subject_token_type`; ask for the downstream `audience`/`resource`. AS authenticates the client, validates the subject, applies a who-may-exchange-for-what policy. Trust only top-level claims plus the current `act`.
- JWT assertions: `iss`, `sub`, `aud` (the AS), `exp` required; client auth `sub` = `client_id`; short `exp`, unique `jti` tracked for replay; never `alg: none`; prefer `private_key_jwt`. Errors: `invalid_grant` (grant), `invalid_client` (client assertion).

## 10. RFC 7662 / 7009 ([§10](oauth-oidc-details.md#10-rfc-7662-and-7009))
- Introspection: authenticated, rate-limited caller; only `active: true` passes (inactive = 200 `{"active": false}`); still check `aud`, `scope`, `exp`, `iss`; cache never beyond `exp`, short TTL. Opaque tokens only; validate JWTs locally.
- Revocation: POST with client auth; 200 also for unknown tokens (not proof of existence); 503 = retry. Call it on logout and when a refresh token is replaced or compromised.

## 11. RFC 8628 ([§11](oauth-oidc-details.md#11-rfc-8628))
- Show `user_code` + `verification_uri` only, never `device_code`. Poll at `interval` (5 s default); `authorization_pending` continue; `slow_down` +5 s; `access_denied`/`expired_token` stop. Wire a `CancellationToken`.
- AS: high-entropy codes, rate-limit `user_code` attempts. No embedded secret in public clients.

## 12. RFC 9449 DPoP ([§12](oauth-oidc-details.md#12-rfc-9449-dpop))
- Proof JWT: `typ: dpop+jwt`, asymmetric `alg` (ES256), public `jwk`, `jti` (96+ bits, unique), `htm`, `htu` (no query/fragment), `iat`, `ath` on resource requests, `nonce` when supplied. New proof per request, retries included. `Authorization: DPoP <token>`.
- RS verifies: one `DPoP` header, signature, `typ`, `htm`/`htu`, `iat` window, `ath`, key thumbprint = `cnf.jkt`, `jti` replay store; rejects DPoP-bound tokens sent as `Bearer`. On `use_dpop_nonce` retry with the newest `DPoP-Nonce`.
- Private key non-exportable where possible, never logged. Server-side DPoP validation is not in the samples.

## Review
Per-area checklist: [../checklists/oauth-oidc.md](../checklists/oauth-oidc.md).
