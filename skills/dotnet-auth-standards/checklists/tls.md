# Code-review checklist: Transport / TLS

For reviewing C# / ASP.NET Core changes. Each item points to the reference section with the rule and example, and to the RFC or specification. Mark an item N/A only if the code does not touch that area. Short rules: see the matching file in `../reference/` (routing in `../SKILL.md`).

Reference files: [X] `../reference/transport-tls.md`.

## 9. Transport / TLS

- [ ] TLS 1.2 and 1.3 accepted, 1.1 and below refused; tested. [X §1](../reference/transport-tls.md#1-version-and-algorithm-policy), RFC 9325, RFC 8996
- [ ] No `DangerousAcceptAnyServerCertificateValidator` and no always-true validation callback. [X §4.1](../reference/transport-tls.md#41-default-validation-is-correct-keep-it), RFC 9525
- [ ] Certificates have SANs; renewal automated; expiry alerts; no private keys in source control. [X §7](../reference/transport-tls.md#7-operational-rules), RFC 9525
- [ ] TLS termination point documented; `ForwardedHeaders` limited to known proxies; `Request.Scheme` is `https`. [X §2](../reference/transport-tls.md#2-kestrel-server-configuration)
- [ ] HSTS on HTTPS responses with a staged `max-age`; no plain-HTTP API listener in production. [X §3](../reference/transport-tls.md#3-hsts-rfc-6797), RFC 6797
- [ ] 0-RTT disabled, or endpoints idempotent and RFC 8470 followed. [X §1](../reference/transport-tls.md#1-version-and-algorithm-policy), RFC 8446, RFC 8470
- [ ] `HttpClient` from a factory with `PooledConnectionLifetime`; custom trust uses a custom store or pinned CA, not blanket acceptance (private CA: `CustomRootTrust` with a custom trust store). [X §4.2, §5.1](../reference/transport-tls.md#42-setting-protocols-and-trust-explicitly), RFC 9525
- [ ] mTLS: `RequireCertificate` only where all callers have certificates; chain, EKU and identity validated; forwarded certificate headers trusted only from the proxy. [X §5](../reference/transport-tls.md#5-mutual-tls), RFC 8705
- [ ] Certificate-bound tokens: `cnf.x5t#S256` compared with the presented certificate; mismatch gives `401 invalid_token`; a `cnf` without `x5t#S256` (e.g. DPoP `jkt`) is read with `TryGetProperty`, not an exception. [X §6](../reference/transport-tls.md#6-certificate-bound-access-tokens-rfc-8705), RFC 8705
- [ ] Tokens never in URLs; internal hops protected. [X §7](../reference/transport-tls.md#7-operational-rules), RFC 6750

## How to report

For each finding give: file and line, the item above, the RFC section, and the fix. Do not claim an item passes without reading the code or running a test for it.
