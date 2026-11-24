# Transport security (TLS 1.3, mTLS) guidelines for C# / ASP.NET Core

Scope: TLS versions and policy (RFC 8446, RFC 9325), certificate and host name validation (RFC 9525, RFC 5280), Kestrel and `HttpClient` configuration, HSTS (RFC 6797), mutual TLS and certificate-bound access tokens (RFC 8705). OAuth flows are in `oauth-oidc.md`, token formats in `tokens-jwt.md`, webhook-specific transport rules (SSRF, HTTPS-only endpoints) in `webhooks-events.md`.

**Sources.** Read from downloaded text (`_sources/http/`, data only), targeted sections only, not the full RFCs: RFC 9325 sections 3.1 to 3.3 and 3.10 (BCP 195, TLS and DTLS recommendations), RFC 8446 (TLS 1.3: section 9.1 mandatory cipher suite, 0-RTT passages), RFC 6797 (HSTS: header examples and the rule about insecure transport), RFC 9525 (service identity: Common Name rule), RFC 8705 (sections 3 and 3.3 and the `x5t#S256` confirmation). RFC 8996 (deprecates TLS 1.0 and 1.1), RFC 7525 (superseded by 9325), RFC 5280 and RFC 6125 (superseded by 9525 for identity checks) are present but were not re-read in detail. **(verified)** marks behaviour observed on a throwaway .NET 10 probe (Kestrel on `https://localhost:5443`, `HttpClient` client, self-signed ECDSA P-256 certificates created in the probe, run on Windows 11, then stopped and deleted). Cipher suite selection on Linux and macOS, certificate-forwarding behind proxies, HTTP/3 and OCSP stapling were **not** tested.

---

## 1. Version and algorithm policy

| Rule | Source |
|---|---|
| MUST NOT negotiate SSL 2.0, SSL 3.0, TLS 1.0 or TLS 1.1 | RFC 9325 section 3.1.1 (in line with RFC 8996) |
| MUST support TLS 1.2 | RFC 9325 |
| SHOULD support TLS 1.3 and prefer it | RFC 9325 |
| SHOULD NOT use TLS-level compression (TLS 1.2; removed in 1.3) | RFC 9325 section 3.3 (CRIME) |
| SHOULD NOT negotiate static-RSA key exchange or non-ephemeral Diffie-Hellman suites in TLS 1.2 | RFC 9325 section 4 (forward secrecy) |
| Applications MUST avoid 0-RTT early data unless a specification for the application protocol says when it is safe | RFC 9325 section 3.10, RFC 8446 section 8 and appendix E.5 |
| A TLS 1.3 implementation MUST implement `TLS_AES_128_GCM_SHA256` | RFC 8446 section 9.1 |

RFC 9325 says that as of its publication TLS 1.2 is as safe as 1.3 when its recommendations are followed, so **accept 1.2 and 1.3, prefer 1.3**, and do not break older clients by requiring 1.3 only unless you control every client (service-to-service, internal mTLS). TLS 1.3 removes renegotiation, compression and static-RSA suites, which is why it is the safer default to configure and test first.

**0-RTT.** Early data is replayable by design (RFC 8446 section 8, appendix E.5). A replayed `POST /payments` would run twice. Do not enable it for HTTP APIs; if a CDN or proxy in front of you does, it must follow RFC 8470 (referenced by RFC 9325) and your endpoints must be idempotent (`http-rest.md`).

**.NET defaults.** In .NET 10 both `SslClientAuthenticationOptions.EnabledSslProtocols` and Kestrel's `HttpsConnectionAdapterOptions.SslProtocols` default to `SslProtocols.None`, meaning "let the operating system decide" **(verified for the client option: prints `None`)**. The OS then applies its own, patchable policy. Setting explicit values is a policy choice for when you must forbid weaker versions on an older host or need an audit trail; it is not needed to get TLS 1.2 or 1.3. Never enable `Ssl2`, `Ssl3`, `Tls` (1.0) or `Tls11`; the probe compiled a `Tls11` client only with `SYSLIB0039` warnings suppressed, which is the compiler flagging the enum value as obsolete.

---

## 2. Kestrel server configuration

```csharp
builder.WebHost.ConfigureKestrel(k =>
{
    k.ConfigureHttpsDefaults(o =>
    {
        o.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;     // explicit policy (default None = OS decides)
        o.ClientCertificateMode = ClientCertificateMode.NoCertificate; // API for browsers/OAuth clients; see section 5 for mTLS
    });
});
```

Rules:
1. **Where TLS terminates.** Either Kestrel terminates it (above) or a reverse proxy/ingress/load balancer does and forwards over a protected internal hop. If the hop is plain HTTP, treat the internal network as untrusted for anything sensitive (tokens, PII) or use TLS on the hop too. Configure `ForwardedHeaders` (`X-Forwarded-Proto`, `X-Forwarded-For`) with `KnownProxies` and the known-networks setting so `Request.Scheme` is `https`; without that, `UseHttpsRedirection` loops and generated absolute URLs (OAuth metadata, `Location`, webhook callbacks, MCP resource URLs) come out as `http://`.
2. **Certificates.** Load from the OS store, a PKCS#12 file with a secret outside source control, or a secret manager (Key Vault, etc.); never commit PFX files or passwords. Use `X509CertificateLoader.LoadPkcs12(...)` (the byte-array `X509Certificate2` constructors are marked obsolete in recent .NET versions; this was not re-checked here, the probe used the loader). Automate renewal; a certificate that expires on a Saturday is an outage. Kestrel reads the certificate at start-up, so plan for a restart or use certificate selection callbacks if you rotate in process.
3. **Keys.** ECDSA P-256 or RSA 2048+ (RFC 9325 section 4.5 on public key length); the probe used ECDSA P-256 and negotiated both TLS 1.2 and TLS 1.3 **(verified)**.
4. **Redirect plain HTTP, but do not rely on it for APIs.** `UseHttpsRedirection` helps browsers. An API client that sent a bearer token over `http://` has already leaked it before the redirect happens. For APIs, do not listen on plain HTTP at all in production, or answer `4xx` there, and revoke any token that was sent over HTTP.
5. **Protocol mix.** Configure `Http1AndHttp2` (and HTTP/3 if you use it) per endpoint via `ListenOptions.Protocols`; HTTP/2 over TLS needs ALPN, which Kestrel negotiates automatically. Long-lived SSE streams (`webhooks-events.md`) benefit from HTTP/2 multiplexing.
6. **Never** ship `app.UseDeveloperExceptionPage()` or the development certificate (`dotnet dev-certs`) to production.

---

## 3. HSTS (RFC 6797)

`Strict-Transport-Security: max-age=31536000; includeSubDomains` tells a browser to use HTTPS only for that host for a year. Observed from the probe **(verified)**: `HSTS=max-age=31536000; includeSubDomains` on the HTTPS response.

```csharp
builder.Services.AddHsts(o =>
{
    o.MaxAge = TimeSpan.FromDays(365);
    o.IncludeSubDomains = true;
    // o.Preload = true;   // only after you commit the whole domain to HTTPS permanently
});
if (!app.Environment.IsDevelopment()) app.UseHsts();
```

Rules:
- An HSTS host **MUST NOT** send the header over non-secure transport (RFC 6797 section 7.2); the middleware sends it on HTTPS responses only.
- Start with a short `max-age` (for example one day), confirm every sub-domain works over HTTPS, then raise it. `includeSubDomains` and `preload` are hard to undo: browsers cache the policy for the whole `max-age`. `max-age=0` clears it (RFC 6797 examples).
- HSTS protects **browsers**. API clients, other services and webhook senders ignore it, so it is not a substitute for HTTPS-only listening and for `https` URL validation in your clients.
- RFC 9325 section 3.2 says web servers SHOULD use HSTS (and warns that it can be problematic with self-signed certificates, because users cannot click through a warning once HSTS applies).
- In my probe I cleared `ExcludedHosts` to see the header on `localhost`. The default exclusion list for loopback names was **not** tested; do not expect HSTS to appear in local development unless you change it.

---

## 4. Client side: HttpClient and certificate validation

### 4.1 Default validation is correct. Keep it.

`HttpClient` validates the chain to a trusted root, expiry, revocation per OS policy, and that the **host name matches a Subject Alternative Name** of the certificate. RFC 9525 section 2 and the later rules: the Common Name RDN MUST NOT be used to identify a service; names come from SAN entries (`dNSName`, `iPAddress`, `uniformResourceIdentifier` for URI-IDs). Issue certificates with correct SANs (the probe's server certificate has `DNS:localhost`) and do not rely on the CN.

**Do not** write this, ever, outside a throwaway test:

```csharp
// BAD: disables validation, enables man-in-the-middle for every request on this client
handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
```

Typical misuse is "it fails on the dev box with a self-signed cert". Fix that by trusting the dev certificate (`dotnet dev-certs https --trust`) or by pinning explicitly (below), not by turning validation off. Search the code base for `DangerousAcceptAnyServerCertificateValidator` and for callbacks that `return true`; treat each hit as a finding.

### 4.2 Setting protocols and trust explicitly

```csharp
var handler = new SocketsHttpHandler
{
    SslOptions = new SslClientAuthenticationOptions
    {
        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
        // Only if you must trust a private CA or pin a certificate; the callback must still say "no" by default.
        RemoteCertificateValidationCallback = (sender, cert, chain, errors) =>
            errors == SslPolicyErrors.None || IsPinned(cert!),
    },
    PooledConnectionLifetime = TimeSpan.FromMinutes(5),       // pick up DNS and certificate rotation
};
```

Notes:
- `SocketsHttpHandler.SslOptions` is the supported place for per-handler TLS policy in .NET 10. With `IHttpClientFactory` use `ConfigurePrimaryHttpMessageHandler`.
- In my probe a client that restricted itself to `Tls11` failed to connect ("Authentication failed"), TLS 1.2 and TLS 1.3 clients succeeded, and the server reported the negotiated protocol (`Tls13`, `Tls12`) through `ITlsHandshakeFeature.Protocol` **(verified)**. Log that value at the edge if you need evidence of what clients actually negotiate.
- **Pinning** (comparing the leaf or public key hash to a stored value) breaks when the certificate rotates; pin the issuing CA or the public key of a long-lived key, and ship a rotation procedure. For a private CA, prefer adding the CA to a custom `X509ChainPolicy.CustomTrustStore` (with `X509ChainTrustMode.CustomRootTrust`) inside the callback, over accepting everything.
- Keep `HttpClient` instances long-lived (via the factory) and set `PooledConnectionLifetime` so rotated certificates and DNS changes take effect.
- Outbound calls to **customer-supplied URLs** need much more than TLS validation (SSRF, no redirects, connect-time IP checks): see `webhooks-events.md`.

---

## 5. Mutual TLS

mTLS authenticates the client with a certificate during the handshake. Typical uses: service-to-service inside a mesh, confidential OAuth clients (RFC 8705), and high-assurance partner APIs.

### 5.1 Server (Kestrel)

```csharp
k.ConfigureHttpsDefaults(o =>
{
    o.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
    // Private CA (not in the OS trust store): validate against YOUR CA only; `errors == None` would reject every client.
    o.ClientCertificateValidation = (cert, _, _) => Validate(cert, clientCa) && IsTrustedClient(cert);
});

static bool Validate(X509Certificate2 cert, X509Certificate2 clientCa)
{
    using var chain = new X509Chain();
    chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
    chain.ChainPolicy.CustomTrustStore.Add(clientCa);
    chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;           // Online + a CRL/OCSP plan in production
    chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.2"));   // clientAuth EKU
    return chain.Build(cert);
}
```

If your clients hold certificates from a **public** CA already in the OS store, the shorter form `chain is not null && errors == SslPolicyErrors.None && IsTrustedClient(cert)` works, but with a private CA `errors` is never `None` and every client is rejected (tested in `examples/ProductionSamples`, `TlsSamples.MutualTlsPrivateCa`).

Probe results with `RequireCertificate`, a validation callback that accepts only one specific client certificate, and a server that echoes `ConnectionInfo.ClientCertificate` **(verified)**:

| Client | Result |
|---|---|
| TLS 1.3, expected client certificate | `200`, server saw subject `CN=client1`, protocol `Tls13` |
| TLS 1.2, expected client certificate | `200`, protocol `Tls12` |
| No client certificate | connection failed on the client (`HttpRequestException`, handshake failure) |
| Certificate from a different key | connection failed on the client |

Rules:
1. **Validate more than "a certificate was presented".** Check the chain to **your** trust anchors (not the whole OS root store, unless that is a deliberate choice), validity, EKU (`clientAuth`, OID 1.3.6.1.5.5.7.3.2) and then authorize the identity (subject DN or SAN against an allow-list). `AllowAnyClientCertificate()` is for tests only.
2. Use `ClientCertificateMode.RequireCertificate` only on endpoints where every caller has a certificate. `AllowCertificate` / `DelayCertificate` allow optional or on-demand certificates, but a client rejected at the handshake sees an opaque failure; prefer separate listeners or host names (for example `mtls.api.example.com`) for mTLS-only traffic. RFC 8705 section 5 describes the same split with `mtls_endpoint_aliases` for authorization servers.
3. In ASP.NET Core, `Microsoft.AspNetCore.Authentication.Certificate` (`AddCertificate`) maps a validated certificate to a `ClaimsPrincipal`. I did **not** run that handler in my probe; consult its documentation for the `ValidateCertificateUse`, `RevocationMode` and `AllowedCertificateTypes` settings, and set revocation checking deliberately: the default online revocation lookup can add latency or fail closed when the responder is unreachable.
4. **Behind a reverse proxy**, the proxy terminates TLS and Kestrel never sees the client certificate. Either pass TLS through, or have the proxy validate the certificate and forward it in a header which ASP.NET Core reads with `UseCertificateForwarding` and `CertificateForwardingOptions.CertificateHeader`. Only accept that header from your proxy (strip it from external requests at the proxy and bind Kestrel to the internal network); otherwise any caller can claim any identity. I did **not** test certificate forwarding.
5. mTLS authenticates the **channel**; still authorize each request (roles, scopes, tenant) and keep short-lived, automatically rotated client certificates (hours or days), preferably issued by an internal CA or a workload identity system (SPIFFE/SPIRE, cert-manager).

### 5.2 Client

```csharp
var handler = new SocketsHttpHandler
{
    SslOptions = new SslClientAuthenticationOptions
    {
        ClientCertificates = new X509CertificateCollection { clientCert },     // loaded via X509CertificateLoader
        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
    },
};
```

Load the private key from a protected store (OS store with non-exportable key, HSM, Key Vault with managed identity). Do not check PFX files into source control.

---

## 6. Certificate-bound access tokens (RFC 8705)

RFC 8705 uses mTLS for two things: **client authentication** at the token endpoint (`tls_client_auth` with a CA-issued certificate matched by subject DN or a SAN, or `self_signed_tls_client_auth` with a registered certificate) and **sender-constrained access tokens**, where the token is bound to the client's certificate so a stolen token cannot be used without the private key.

How the binding works:
- The authorization server puts the certificate's thumbprint in the token as the confirmation claim `cnf`: `"cnf": {"x5t#S256": "<value>"}` (JWT access token or introspection response).
- `x5t#S256` is the **base64url-encoded SHA-256 hash of the DER-encoded certificate**, **with trailing `=` padding omitted** (RFC 8705 section 3.1).
- The resource server **MUST** obtain the client certificate from its TLS layer and **MUST** verify that it matches the certificate bound to the token. On mismatch the request **MUST** be rejected with `401` and `error="invalid_token"` per RFC 6750.
- The client must use the **same certificate** at the token endpoint and the resource server (RFC 8705 section 3).
- Authorization server metadata (RFC 8414) advertises `tls_client_certificate_bound_access_tokens`; the default when omitted is `false` (RFC 8705 section 3.3).

Resource-server check (in the probe I verified that `SHA256.HashData(ClientCertificate.RawData)` plus base64url and padding removal yields the value the server echoed; the comparison against a token `cnf` was not run against a real token):

```csharp
static string X5tS256(X509Certificate2 cert)
    => Convert.ToBase64String(SHA256.HashData(cert.RawData)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

// in an authorization handler or JwtBearer OnTokenValidated event
var cert = ctx.HttpContext.Connection.ClientCertificate;           // null if no mTLS client cert
var cnf  = ctx.Principal?.FindFirst("cnf")?.Value;                  // JSON: {"x5t#S256":"..."}
string? bound = null;   // TryGetProperty: a cnf with only "jkt" (DPoP) must not throw
if (cnf is not null && JsonDocument.Parse(cnf).RootElement.TryGetProperty("x5t#S256", out var v)) bound = v.GetString();
if (bound is not null && (cert is null || !CryptographicOperations.FixedTimeEquals(
        Encoding.ASCII.GetBytes(bound), Encoding.ASCII.GetBytes(X5tS256(cert)))))
    ctx.Fail("certificate does not match token binding");           // answer 401 invalid_token
```

Adapt the claim extraction to how your JWT handler surfaces `cnf` (it may arrive as a JSON claim value or you may read it from `SecurityToken`); the structure above is what to preserve: a token **with** `cnf` is only valid when the presented certificate matches, and a token **without** `cnf` must not be treated as bound. If you only want DPoP (application-layer sender constraint, no mTLS), see `oauth-oidc.md`.

---

## 7. Operational rules

- **Certificate lifecycle:** inventory certificates, alert at 30 and 7 days before expiry, automate issuance (ACME or internal CA).
- **Revocation:** know what your platform does when OCSP/CRL is unreachable; for mTLS between your own services short-lived certificates are usually a better answer than relying on revocation lists.
- **Test your policy** like any control: an automated check that `https://` works with TLS 1.2 and 1.3 and fails for TLS 1.1 (as in the probe), and that an expired or wrong-host certificate is rejected.
- **Logging:** log negotiated protocol, client certificate subject and thumbprint (not the key, not the whole certificate), and handshake failures at the edge.
- **Secrets in transit:** never put tokens or API keys in URLs (they land in proxy and server logs even over TLS); use headers or the body.
- **Internal traffic:** "inside the VPC" is not a TLS exemption for credentials; use TLS or a mesh with mTLS.

---

## 8. Do and Don't

**Do**
- Accept TLS 1.2 and 1.3; prefer 1.3; refuse 1.1 and below. Leave `SslProtocols.None` unless you need an explicit policy.
- Keep default chain and host name validation; issue certificates with SANs.
- Terminate TLS somewhere you control; set forwarded-headers trust correctly.
- Use `X509CertificateLoader`, secret stores and automatic renewal.
- Send HSTS on HTTPS responses (start with a short `max-age`).
- For mTLS: validate chain, EKU and identity; use short-lived certificates; separate mTLS host names; compare the certificate to `cnf.x5t#S256` for bound tokens.

**Don't**
- Don't use `DangerousAcceptAnyServerCertificateValidator` or a callback that always returns `true`.
- Don't identify servers by Common Name; don't skip SAN checks.
- Don't enable 0-RTT for non-idempotent endpoints.
- Don't listen on plain HTTP in production for APIs that carry bearer tokens.
- Don't trust a forwarded client-certificate header from the open internet.
- Don't commit PFX files or certificate passwords; don't share one certificate across many services.
- Don't preload HSTS before every sub-domain is HTTPS-ready.

---

## 9. Checklist

- [ ] TLS 1.2 and 1.3 accepted, 1.1 and below refused; tested (protocol logged at the edge).
- [ ] No `DangerousAcceptAnyServerCertificateValidator`, no always-true validation callback in the repository.
- [ ] Certificates have SANs; renewal automated; expiry alerts; no secrets in source control.
- [ ] TLS termination point documented; `ForwardedHeaders` limited to known proxies; `Request.Scheme` is `https`.
- [ ] HSTS enabled on HTTPS responses with a staged `max-age`; no plain-HTTP API listener in production.
- [ ] 0-RTT disabled (or endpoints idempotent and RFC 8470 followed).
- [ ] `HttpClient` created via factory with `PooledConnectionLifetime`; custom trust uses a custom trust store or pinned CA, not blanket acceptance.
- [ ] mTLS: `RequireCertificate` only where all callers have certificates; chain, EKU and identity validated; forwarded certificate headers accepted only from the proxy.
- [ ] Certificate-bound tokens: `cnf.x5t#S256` compared (base64url SHA-256 of DER, no padding); mismatch gives `401 invalid_token`.
- [ ] Tokens never in URLs; internal hops protected.

---

## 10. What was not covered

- Cipher suite configuration (`CipherSuitesPolicy` is not available on every OS; Windows uses SChannel policy) and OCSP stapling.
- Behaviour of `Microsoft.AspNetCore.Authentication.Certificate` and certificate forwarding in a real proxy chain.
- HTTP/3 and QUIC requirements, DTLS, ECH, certificate transparency, ACME client code.
- The full text of RFC 8446, RFC 9325, RFC 8705 and RFC 6797: only the sections cited above were read.
