# Webhooks and events guidelines for C# / ASP.NET Core

Scope: sending and receiving webhooks (Standard Webhooks 1.0.0 signature scheme, HMAC-SHA256 per RFC 2104), delivery reliability and SSRF defence on the sender side, CloudEvents 1.0.2 and its HTTP binding and HTTP webhook delivery profile, and Server-Sent Events (SSE). TLS is in `transport-tls.md`; HTTP status and idempotency semantics in `http-rest.md`; bearer tokens in `oauth-oidc.md`; MCP's own streaming transport in `mcp-ai.md`.

**Sources.** Read from downloaded text (`_sources/webhooks/`, data only): Standard Webhooks specification v1.0.0 (a community specification under the Apache 2.0 licence, **not** an IETF standard); CloudEvents v1.0.2 core spec, HTTP protocol binding, and HTTP 1.1 Web Hooks for Event Delivery; the WHATWG HTML Standard section on Server-Sent Events (`sse.txt`); RFC 2104 (HMAC). The Standard Webhooks "endpoint verification" extension was listed in the spec but its document was not read. **(verified)** marks behaviour observed on throwaway .NET 10 probes (run, output checked, deleted): a verifier and SSRF-guard probe with 26 checks (all PASS), and an SSE endpoint probe.

---

## 1. Which standard for which problem

| Need | Use | Status |
|---|---|---|
| Authenticate and de-duplicate inbound HTTP callbacks | Standard Webhooks headers and signature | community spec 1.0.0 |
| A common envelope for event metadata across systems | CloudEvents (`id`, `source`, `type`, `specversion`) | CNCF spec 1.0.2 |
| Deliver CloudEvents to an HTTP endpoint | CloudEvents HTTP binding + "HTTP 1.1 Web Hooks" profile | spec documents |
| Server pushes a one-way stream to a browser or client | SSE (`text/event-stream`) | WHATWG HTML Standard |

They combine. Standard Webhooks signs *how* a request arrives; CloudEvents describes *what* the event is. You may put a CloudEvents structured-mode JSON body inside a Standard-Webhooks-signed request, but the two specs do not define that combination, so document it in your own API.

---

## 2. Payload design (Standard Webhooks "Payload")

The spec gives recommendations, not requirements:

- POST the payload in the HTTP body, JSON by preference.
- Fields: `type` (full-stop delimited, hierarchical, e.g. `invoice.paid`), `timestamp` (ISO 8601, when the event **occurred**, not when it was delivered), `data`.
- Event types limited to `[a-zA-Z0-9_]` identifiers separated by `.`. A given type always has the same schema, like a URL path in REST. Let consumers subscribe per type and filter on your side.
- Keep payloads small: the spec says usually under 20 KB. For big data, send a link.
- **Thin vs full.** Thin payloads (ids only) are cheaper, more future-proof and keep data access auditable because the consumer must call your API (which authenticates and logs). Full payloads save a round trip. Mixing is fine (an id plus one commonly used field).

```csharp
public sealed record WebhookEnvelope<T>(string Type, DateTimeOffset Timestamp, T Data);

// {"type":"invoice.paid","timestamp":"2026-11-24T10:15:00Z","data":{"id":"inv_123"}}
```

**Do** document each event type with a JSON Schema or OpenAPI webhook definition (`api-design.md`). **Do** add new optional fields rather than changing existing ones; consumers must ignore unknown properties.

---

## 3. Headers and the signature scheme

Three headers, exact names, on every delivery:

| Header | Content |
|---|---|
| `webhook-id` | Unique id of the **message**; **identical on every retry** of that message |
| `webhook-timestamp` | Integer Unix time in **seconds** of **this attempt** (changes on each retry) |
| `webhook-signature` | **Space-delimited list** of `version,base64signature` entries |

Signed content, symmetric (`v1`): `msg_id.timestamp.payload` (full stops between, payload = the exact body bytes as UTF-8 text). Signature = `base64(HMAC-SHA256(key = secret, msg = signed content))`, header entry `v1,<base64>`.

Asymmetric (`v2a`): content is `endpoint_id.msg_id.timestamp.payload`; the **raw 32-byte SHA-256 hash** of that content is signed with pure ed25519; entry `v2a,<base64>`. The endpoint id is chosen by the producer, unique per endpoint, constant, contains no `.`, and is **not** sent with the webhook: the producer shares it with the consumer together with the public key. `v1a` is deprecated.

Secret formats: `whsec_` + base64 (symmetric key, random, 24 to 64 bytes), `whsk_` / `whpk_` + base64 (ed25519 private / public key). Use the **base64-decoded bytes** after the prefix as the HMAC key, not the string.

Rules from the spec you must not skip:
- Message id and timestamp must not be user controlled, or at least must not contain `.`; reject ids with a dot on receipt **(verified: id `msg.5` rejected as malformed)**.
- Symmetric keys are **unique per endpoint**; never reuse a key across customers.
- Prefer asymmetric signatures when you do not control both ends; the spec says to weigh the speed of HMAC against its security drawbacks. With HMAC, anyone holding the secret can also forge.
- Consumers keep a trust list of public keys; never read the key from the request.

---

## 4. Receiving webhooks in ASP.NET Core

### 4.1 Read the raw body

The signature covers the exact bytes. The spec calls out the usual failure: parse JSON, serialize it again, signature breaks. In a minimal API do **not** bind the body to a model first. Take `HttpRequest`, read the bytes, verify, then deserialize **from the same bytes**.

```csharp
app.MapPost("/webhooks/billing", async (HttpRequest req, WebhookVerifier verifier, IBackgroundQueue queue) =>
{
    if (req.ContentLength is null or > 262_144) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

    using var ms = new MemoryStream();
    await req.Body.CopyToAsync(ms, req.HttpContext.RequestAborted);
    var raw = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);

    var id = req.Headers["webhook-id"].ToString();
    var result = verifier.Verify(
        id,
        req.Headers["webhook-timestamp"].ToString(),
        req.Headers["webhook-signature"].ToString(),
        raw);

    switch (result)
    {
        case VerifyResult.Ok:        await queue.EnqueueAsync(raw); verifier.MarkProcessed(id); return Results.Accepted();   // 202; mark only after enqueue
        case VerifyResult.Duplicate: return Results.Ok();                                          // already handled: success, not error
        default:                     return Results.Unauthorized();                                // same answer for all failures
    }
});
```

Note: `ContentLength is null` is also true for **chunked** requests, so they get 413 too. That is acceptable for signed webhooks (senders send a known length); if your sender chunks, drop that check and rely on a capped read (`MaxRequestBodySize`, or stop copying past the limit).

Rules:
1. **Verify before you deserialize or act.** Unauthenticated bytes get no further parsing than the signature check.
2. **Bound the body size** before reading (Kestrel `MaxRequestBodySize`, or the check above); an unauthenticated endpoint is an anonymous upload endpoint.
3. **Acknowledge fast, process later.** The sender treats any non-2xx or timeout as failure and retries; the spec recommends 15 to 30 s sender timeouts. Verify, persist or enqueue, return 2xx, do the work out of band.
4. Return one generic status (401) for every verification failure; do not tell an attacker whether the timestamp, the signature or the id was wrong. Log the reason server-side with the `webhook-id`.
5. If you return a body for errors, use RFC 9457 Problem Details (`http-rest.md`).

### 4.2 The verifier (probe-tested)

Everything below passed in a probe: valid signature, replay of the same id (rejected), tampered body, one-hour-old timestamp, rotation list with a bad entry first and a good entry second, dot in id, non-numeric timestamp **(verified)**.

```csharp
enum VerifyResult { Ok, Malformed, StaleTimestamp, BadSignature, Duplicate }

sealed class WebhookVerifier(IReadOnlyList<byte[]> secrets, IMemoryCache seen, TimeSpan tolerance)
{
    public VerifyResult Verify(string id, string timestamp, string signatureHeader, string rawBody)
    {
        if (id.Length is 0 or > 128 || id.Contains('.')) return VerifyResult.Malformed;
        // NumberStyles.None + invariant culture: plain digits only ("+5", " 5 ", "-5" would otherwise parse).
        if (!long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var ts)) return VerifyResult.Malformed;
        // FromUnixTimeSeconds throws above 253402300799: "99999999999999" would be a 500 on an anonymous endpoint.
        if (ts is < 0 or > 253_402_300_799) return VerifyResult.Malformed;

        var age = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(ts);
        if (age > tolerance || age < -tolerance) return VerifyResult.StaleTimestamp;       // both directions

        var content = Encoding.UTF8.GetBytes($"{id}.{ts}.{rawBody}");
        var ok = false;
        Span<byte> candidate = stackalloc byte[32];                                          // hoisted: stackalloc in a loop fails CA2014
        foreach (var secret in secrets)                                                      // current + previous secret
        {
            var expected = HMACSHA256.HashData(secret, content);
            foreach (var part in signatureHeader.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!part.StartsWith("v1,", StringComparison.Ordinal)) continue;            // ignore v2a etc. here
                if (Convert.TryFromBase64String(part[3..], candidate, out var n) && n == 32
                    && CryptographicOperations.FixedTimeEquals(candidate, expected))
                    ok = true;                                                               // no early exit
            }
        }
        if (!ok) return VerifyResult.BadSignature;

        // Only CHECK here. Recording the id before the work is queued loses the event if the enqueue fails:
        // the sender's retry (same webhook-id) would be answered as a duplicate.
        return seen.TryGetValue(id, out _) ? VerifyResult.Duplicate : VerifyResult.Ok;
    }

    public void MarkProcessed(string id) => seen.Set(id, true, tolerance * 2);               // call AFTER the enqueue succeeded
}
```

Why each line:
- `CryptographicOperations.FixedTimeEquals` on decoded bytes: the spec requires a constant-time comparison for symmetric signatures, otherwise the consumer becomes a signing oracle. Never use `==` or `string.Equals` on signatures.
- The header is a **list**: loop over all entries and accept if **any** matches. This is how the sender rotates secrets without downtime (it signs with the old and the new key and sends both). The probe's `"v1,AAAA <good>"` header was accepted **(verified)**.
- Timestamp window both directions (spec: "within some allowable tolerance"); the spec does not give a number. Five minutes is a common choice and what the probe used; the spec's own idempotency example stores ids for 5 minutes.
- The `webhook-id` store makes replays inside the window harmless. **`IMemoryCache` works only for one instance.** With several instances use a shared store with an atomic set-if-not-exists (Redis `SET NX EX`, or a unique constraint in your database) and record the id **after** the work is durably queued, so a crash does not turn into a lost event.
- Mark the id as seen only **after** the signature verifies. Otherwise an attacker can burn legitimate ids.
- Load secrets from configuration or a secret store; keep the `whsec_` prefix stripped and base64-decoded once at startup.

### 4.3 Idempotent processing

The spec: `webhook-id` "remains the same no matter how many times a webhook that has failed is retried", so it is the idempotency key. The signature replay window covers only minutes; a retry can arrive hours later with a **fresh timestamp** and a valid signature, so the window check will not stop it. Persist processed ids for at least as long as the sender's retry schedule (the spec example runs to about 75 hours) and design handlers so repeated processing has the same result. Also expect **out-of-order** delivery: use `timestamp` in the payload or a version number to ignore stale updates.

### 4.4 Endpoint exposure

- HTTPS only (spec "Enforcing HTTPS": the signature gives authenticity, not confidentiality; the CloudEvents web hook profile says HTTPS MUST be used).
- Allow-list the sender's static IPs at the firewall if the producer publishes them, as a second layer, never as the only one.
- Rate limit the anonymous endpoint (`AddRateLimiter`), but return 429 with `Retry-After` so well-behaved senders back off (CloudEvents web hook profile: sender MUST observe `Retry-After`).
- Return `410 Gone` when you retire an endpoint; the spec tells senders to stop delivering.

---

## 5. Sending webhooks

### 5.1 Delivery semantics (Standard Webhooks "Operational considerations")

- **Success = any 2xx.** Everything else is failure: non-2xx, timeout, connection reset.
- 3xx is a **failure**; do not follow redirects (the spec says to update the URL instead; the CloudEvents web hook profile also says clients MUST NOT follow 3xx).
- `410 Gone`: disable the endpoint and stop. `429`, `502`, `504`: throttle. Honour `Retry-After` on any response (for example 503).
- Timeout 15 to 30 s per attempt.
- Retry with exponential backoff **and jitter**, over days. The spec's example schedule: immediately, 5 s, 5 min, 30 min, 2 h, 5 h, 10 h, 14 h, 20 h, 24 h (cumulative about 75 h 35 min).
- After long consistent failure, notify the customer by another channel and disable the endpoint.
- Give customers a delivery log, manual replay, and an endpoint-management API (spec "Additional functionality"; optional, but you will need them in support).

```csharp
static class RetryPolicy
{
    static readonly TimeSpan[] Schedule = [TimeSpan.Zero, TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(30), TimeSpan.FromHours(2), TimeSpan.FromHours(5), TimeSpan.FromHours(10),
        TimeSpan.FromHours(14), TimeSpan.FromHours(20), TimeSpan.FromHours(24)];

    public static TimeSpan Delay(int attempt, Random rng)                                    // attempt 0 = first
        => Schedule[Math.Min(attempt, Schedule.Length - 1)] * (0.8 + rng.NextDouble() * 0.4);   // +/-20% jitter
}
```

Run retries from a durable queue (database outbox, Service Bus, Hangfire, Quartz), not from `Task.Delay` in a request. Write the event to your database in the **same transaction** as the state change (transactional outbox), or a crash between commit and send loses the event.

### 5.2 Signing

```csharp
static string Sign(byte[] secret, string id, long unixSeconds, string body)
    => "v1," + Convert.ToBase64String(HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes($"{id}.{unixSeconds}.{body}")));
```

- Serialize the body **once** to a `string` or `byte[]`; sign and send that same value (`new StringContent(body, Encoding.UTF8, "application/json")`). Do not let `JsonContent.Create` serialize a second time.
- Generate a fresh timestamp for each attempt, keep the `webhook-id` identical.
- Generate secrets with `RandomNumberGenerator.GetBytes(32)` (inside the spec's 24 to 64 byte range) and show them to the customer once in `whsec_` form.
- Rotation: send `v1,<new> v1,<old>` for a bounded period, then drop the old.
- Sign with a KMS or HSM-held ed25519 key (`v2a`) when you can; the spec says private keys should never be exported or shown to customers.

### 5.3 SSRF: the customer supplies the URL

The spec calls webhook senders "especially vulnerable" to SSRF: an attacker registers `http://169.254.169.254/...` or an internal service as the endpoint. The spec's remedy is infrastructure: send through a filtering proxy (it names smokescreen) and run workers in a private subnet that cannot reach internal services. Do that. In addition, filter in code, and **filter at connect time**, not by checking the URL string, because DNS can return a different address on the second lookup (rebinding) and a check-then-connect has a race.

Probe result: a plain `HttpClient` POSTed to `127.0.0.1` successfully (the risk), while the guarded client rejected `127.0.0.1`, `localhost` and `[::ffff:127.0.0.1]` **(verified)**. The guard also blocked `10/8`, `172.16/12`, `192.168/16`, `169.254/16` (cloud metadata), `100.64/10`, `0.0.0.0`, `::1`, `fe80::/10`, `fc00::/7`, IPv4-mapped IPv6 forms, and allowed public addresses **(verified)**.

```csharp
public static HttpClient CreateClient() => new(new SocketsHttpHandler
{
    AllowAutoRedirect = false,                         // 3xx = failed delivery; never follow into the intranet
    ConnectTimeout = TimeSpan.FromSeconds(5),
    UseCookies = false,
    ConnectCallback = async (ctx, ct) =>
    {
        var addrs = await Dns.GetHostAddressesAsync(ctx.DnsEndPoint.Host, ct);              // resolve once...
        var ip = addrs.FirstOrDefault(a => !IsBlocked(a))
            ?? throw new HttpRequestException($"Destination {ctx.DnsEndPoint.Host} is blocked.");
        var socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try { await socket.ConnectAsync(new IPEndPoint(ip, ctx.DnsEndPoint.Port), ct); } // ...connect to that exact IP
        catch { socket.Dispose(); throw; }
        return new NetworkStream(socket, ownsSocket: true);
    },
}) { Timeout = TimeSpan.FromSeconds(20) };
```

`IsBlocked` must first unmap IPv4-mapped IPv6 (`ip.MapToIPv4()`), or `::ffff:169.254.169.254` slips through; the probe tested that case **(verified)**. Additional rules:
- Require `https` URLs at registration (spec: "Enforcing HTTPS"); allow `http` only in development.
- Resolve with `ConnectCallback`; with this handler TLS still validates the host name, because the `SslStream` is created by the handler on top of the stream you return from the callback and uses the original host name (the callback only supplies the TCP stream). I did not run a TLS test against the guarded client, so confirm with your own test before relying on it.
- Do not send your own `Authorization` headers or cookies to customer URLs. Cap the response size you read; you only need the status code and a small snippet for logs.
- Use the guarded client through `IHttpClientFactory` with `ConfigurePrimaryHttpMessageHandler`, keeping the callback.

### 5.4 Registration checks

- Optionally verify the endpoint before the first delivery. Standard Webhooks lists an "endpoint verification" extension (not read here); the CloudEvents web hook profile defines its own OPTIONS handshake (section 6.3).
- Never log the secret or the full signature header; log `webhook-id`, status code and latency.

---

## 6. CloudEvents 1.0.2

### 6.1 Required attributes

`id` (unique per `source`), `source` (URI-reference identifying the context), `specversion` (`"1.0"`), `type` (event type, reverse-DNS prefix recommended, e.g. `com.example.invoice.paid`). Optional: `datacontenttype`, `dataschema`, `subject`, `time` (RFC 3339), `data`. Extension attributes are allowed. A consumer must treat `source` + `id` as the de-duplication key.

### 6.2 HTTP binding content modes

| Mode | Wire format |
|---|---|
| **Binary** | Event attributes become HTTP headers prefixed `ce-` (`ce-id`, `ce-source`, `ce-specversion`, `ce-type`, `ce-time`); the HTTP `Content-Type` is the `datacontenttype`; the body is the data only. A `ce-datacontenttype` header MUST NOT also be sent |
| **Structured** | Whole event in the body in an event format (JSON), `Content-Type: application/cloudevents+json` |
| **Batched** | Several events in one body, `Content-Type: application/cloudevents-batch+json`; MUST NOT be used unless solicited by the receiver |

A receiver tells the modes apart by `Content-Type`: prefix `application/cloudevents` means structured, `application/cloudevents-batch` means batched, anything else is **binary**. Support structured JSON at minimum (the binding requires it for implementations that support structured mode). Binary mode keeps the payload untouched, which suits proxies and signatures; structured mode is easiest to log and replay.

Binary-mode example (from the binding): `ce-specversion: 1.0`, `ce-type: com.example.someevent`, `ce-time: 2018-04-05T03:56:24Z`, `ce-id: 1234-1234-1234`, `ce-source: /mycontext/subcontext`, `Content-Type: application/json; charset=utf-8`.

With the CloudEvents .NET SDK (`CloudNative.CloudEvents` and `CloudNative.CloudEvents.AspNetCore`) you can parse both modes. I did **not** run that SDK in a probe, so check the current API against its documentation before copying a snippet from memory.

### 6.3 HTTP 1.1 Web Hooks for event delivery

Requirements (spec section 2 and 3), summarised:
- HTTPS MUST be used; method MUST be POST; `Content-Type` MUST be present and a payload MUST be present (events carried only in headers are not allowed).
- Response: 3xx MUST NOT be used and clients MUST NOT follow; `200`/`201` with a body that has `Content-Type`; `201`/`204` with no body; `202` accepted but not yet processed; `410` retired target, sender should stop; `429` with a **required** `Retry-After` the sender MUST observe; `415` for an unknown format.
- Authorization: the delivery uses a bearer-style token (`Authorization: Bearer ...`, or the token as the `access_token` query parameter) following the RFC 6750 model; challenge-based schemes MUST NOT be used. The target must support both methods. Prefer the header: query strings end up in logs.
- **Abuse protection** (section 4): before sending, the sender may ask the target to opt in with an HTTP `OPTIONS` request that carries `WebHook-Request-Origin` (the sender's DNS name) and optionally `WebHook-Request-Callback` and `WebHook-Request-Rate`. The target answers with `WebHook-Allowed-Origin` (the origin or `*`) and `WebHook-Allowed-Rate` (and an `Allow` header). A target that handles OPTIONS but does not support abuse protection answers as if OPTIONS were unsupported (405). This protects third parties from being flooded by a sender that was pointed at them.

Note this profile authenticates with bearer tokens, **not** the Standard Webhooks signature. Pick one scheme per endpoint and document it; do not claim an endpoint is "CloudEvents webhook compliant" and "Standard Webhooks compliant" without implementing both header sets.

---

## 7. Server-Sent Events

SSE is one-way server-to-client over a long-lived HTTP response with `Content-Type: text/event-stream`. The browser `EventSource` API reconnects automatically and sends a `Last-Event-ID` request header carrying the last `id:` it saw (HTML Standard, EventSource). Limits to design around: `EventSource` cannot set custom headers (no `Authorization`) in browsers, GET only, text only. Non-browser clients (including .NET `System.Net.ServerSentEvents` `SseParser`) have none of these limits. MCP's streamable HTTP transport uses SSE for server messages (`mcp-ai.md`).

.NET 10 ships `TypedResults.ServerSentEvents`. Probe results **(verified)**:

```csharp
app.MapGet("/events", (HttpContext ctx, CancellationToken ct) =>
{
    var last = ctx.Request.Headers["Last-Event-ID"].ToString();       // resume point sent by EventSource
    return TypedResults.ServerSentEvents(Gen(last, ct));               // IAsyncEnumerable<SseItem<string>>
});

static async IAsyncEnumerable<SseItem<string>> Gen(string last, [EnumeratorCancellation] CancellationToken ct)
{
    int start = int.TryParse(last, out var n) ? n + 1 : 1;
    for (int i = start; ; i++)
    {
        yield return new SseItem<string>($"payload {i}", "tick") { EventId = i.ToString(), ReconnectionInterval = TimeSpan.FromSeconds(3) };
        await Task.Delay(1000, ct);
    }
}
```

Observed on the wire: `Content-Type: text/event-stream`, `Cache-Control: no-cache,no-store`, chunked transfer, and each item as `event: tick`, `data: payload 8`, `id: 8`, `retry: 3000`, blank line. A request with `Last-Event-ID: 7` yielded events 8, 9, 10 **(verified)**.

**Pitfall (verified):** passing `eventType: "tick"` together with an `IAsyncEnumerable<SseItem<string>>` made the framework treat each `SseItem` as plain data, so the `data:` line contained the JSON of the whole item (`{"data":"payload 1","eventType":"tick","eventId":"1",...}`) and **no `id:` line was emitted**, which silently breaks resumption. Put the event type on the `SseItem` and call the overload without `eventType`.

Rules:
1. **Assign an `id` to every event you want to be resumable** and honour `Last-Event-ID` on reconnect by replaying from a durable log; if you cannot replay, say so and send a full-state event first.
2. **Authenticate the connection** as for any endpoint. Because browser `EventSource` cannot send `Authorization`, use a cookie session, a short-lived single-use ticket in the URL, or a fetch-based client; never put long-lived access tokens in the query string.
3. Honour `CancellationToken` so a disconnect ends the loop; each open stream pins a connection, so cap per-user streams and set idle timeouts.
4. Send a comment or keep-alive event periodically so proxies and load balancers do not drop idle connections; verify your proxy does not buffer `text/event-stream` (disable response buffering and compression for the route).
5. With HTTP/1.1, browsers limit connections per host; prefer HTTP/2 or HTTP/3 for many streams (`transport-tls.md`).
6. Use SSE for notifications to a connected client; use webhooks for server-to-server and durable delivery. SSE gives no delivery guarantee beyond what you build with `id` and replay.

---

## 8. Do and Don't

**Do**
- Verify `webhook-signature` over `id.timestamp.rawBody` with a constant-time comparison **before** parsing.
- Accept any matching entry in the space-delimited signature list (rotation).
- Check the timestamp in both directions and de-duplicate on `webhook-id` in a shared, durable store.
- Return 2xx quickly and process asynchronously; use a transactional outbox when sending.
- Retry with exponential backoff and jitter; disable and notify on prolonged failure; honour `Retry-After`, `410`, `429`.
- Use HTTPS-only endpoints; per-endpoint secrets; asymmetric signatures where you can.
- Connect-time IP filtering plus no redirects plus network isolation for outbound delivery.
- Give every resumable SSE event an `id`, and test `Last-Event-ID`.

**Don't**
- Don't deserialize and re-serialize the body before checking the signature.
- Don't compare signatures with `==`; don't take the key or endpoint id from the request.
- Don't treat the timestamp window as replay protection against late retries; store ids.
- Don't follow redirects or accept private/loopback/link-local/metadata addresses for customer URLs; don't check the URL string only.
- Don't reuse one HMAC secret across customers; don't log secrets.
- Don't put a bearer token in the query string if a header is possible.
- Don't pass `eventType:` with an `SseItem` stream to `TypedResults.ServerSentEvents` (drops `id`, wraps data in JSON).

---

## 9. Checklist

**Receiver**
- [ ] Raw bytes read once; size limit applied; verification before deserialization.
- [ ] `webhook-id` (no dots), `webhook-timestamp` (integer, both-direction window), `webhook-signature` (list, `v1,` entries) parsed strictly.
- [ ] HMAC-SHA256 over `id.timestamp.body`, base64-decoded `whsec_` key, `FixedTimeEquals`.
- [ ] Secret list supports current and previous key.
- [ ] Idempotency store shared across instances, retention longer than the sender's retry span.
- [ ] 2xx returned fast; work queued; one generic 401 for failures; reason logged server-side.
- [ ] HTTPS only; rate limit with `Retry-After`.

**Sender**
- [ ] Unique `webhook-id` per message, new `webhook-timestamp` per attempt; body serialized once.
- [ ] Per-endpoint random secret (24 to 64 bytes), rotation supported; or `v2a` with an endpoint id.
- [ ] Retry schedule with jitter from a durable queue; transactional outbox.
- [ ] 2xx only is success; 3xx, 410, 429, 502, 504 handled as above; 15 to 30 s timeout.
- [ ] SSRF: `ConnectCallback` IP filter (IPv4-mapped IPv6 unmapped), no redirects, isolated subnet or proxy.
- [ ] Delivery log, manual replay, auto-disable plus notification.

**CloudEvents / SSE**
- [ ] `id`, `source`, `specversion`, `type` present; content mode chosen and documented; batch only when solicited.
- [ ] Web hook profile: HTTPS, POST, bearer authorization, no 3xx, `Retry-After` on 429, OPTIONS handshake if abuse protection is offered.
- [ ] SSE: `text/event-stream`, `id` on events, `Last-Event-ID` replay, keep-alives, auth that works without custom headers in browsers, cancellation honoured.

---

## 10. What was not covered

- The Standard Webhooks endpoint-verification extension document (listed in the spec, not read).
- ed25519 `v2a` verification code (specified above, **not** run in a probe; use a maintained library, as the spec advises).
- The CloudEvents .NET SDK, Dapr/Event Grid/Azure-specific delivery, and AsyncAPI.
- RFC 9421 HTTP Message Signatures is an alternative way to sign requests (the source text is in `_sources/http/`) but is not part of Standard Webhooks and was not evaluated here.
