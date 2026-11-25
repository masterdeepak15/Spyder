# Code-review checklist: Webhooks and events

For reviewing C# / ASP.NET Core changes. Each item points to the reference section with the rule and example, and to the RFC or specification. Mark an item N/A only if the code does not touch that area. Short rules: see the matching file in `../reference/` (routing in `../SKILL.md`).

Reference files: [W] `../reference/webhooks-events.md`.

## 7. Webhooks and events

Standard Webhooks and CloudEvents are community specifications, not RFCs; the signature primitive is HMAC (RFC 2104). SSE is the WHATWG HTML Standard.

- [ ] Receiver reads the raw bytes once, applies a size limit, and verifies the signature before deserializing; unknown length (chunked) is rejected 413 or read with a capped stream. [W §4.1](../reference/webhooks-events.md#41-read-the-raw-body), Standard Webhooks
- [ ] `webhook-id`, `webhook-timestamp` (window in both directions) and `webhook-signature` (list of `v1,` entries) parsed strictly (timestamp digits only: `NumberStyles.None`, invariant culture). [W §3](../reference/webhooks-events.md#3-headers-and-the-signature-scheme), Standard Webhooks
- [ ] HMAC-SHA256 over `id.timestamp.body` with the base64-decoded `whsec_` key, compared with `FixedTimeEquals`; current and previous secret accepted during rotation. [W §4.2](../reference/webhooks-events.md#42-the-verifier-probe-tested), RFC 2104
- [ ] Idempotency store shared across instances, retention longer than the sender's retry span; id marked processed only after the enqueue succeeded; 2xx returned fast, work queued, one generic 401 for failures. [W §4.3](../reference/webhooks-events.md#43-idempotent-processing), Standard Webhooks
- [ ] Sender: unique `webhook-id` per message, new timestamp per attempt, per-endpoint random secret with rotation. [W §5.2](../reference/webhooks-events.md#52-signing), Standard Webhooks
- [ ] Sender: retries with jitter from a durable queue; only 2xx is success; timeout set; delivery log, replay and auto-disable exist. [W §5.1](../reference/webhooks-events.md#51-delivery-semantics-standard-webhooks-operational-considerations), Standard Webhooks
- [ ] Sender: customer-supplied URLs are guarded against SSRF (`ConnectCallback` IP filter, IPv4-mapped IPv6 unmapped, no redirects). [W §5.3](../reference/webhooks-events.md#53-ssrf-the-customer-supplies-the-url), Standard Webhooks
- [ ] CloudEvents: `id`, `source`, `specversion`, `type` present; content mode chosen and documented. [W §6.1, §6.2](../reference/webhooks-events.md#62-http-binding-content-modes), CloudEvents 1.0.2
- [ ] CloudEvents web hook delivery: HTTPS, POST, no 3xx, `Retry-After` on 429, OPTIONS handshake if abuse protection is offered. [W §6.3](../reference/webhooks-events.md#63-http-11-web-hooks-for-event-delivery), CloudEvents HTTP 1.1 Web Hooks
- [ ] SSE: `text/event-stream`, event `id` with `Last-Event-ID` replay, keep-alives, browser-compatible auth, cancellation honoured. [W §7](../reference/webhooks-events.md#7-server-sent-events), WHATWG HTML Standard

## How to report

For each finding give: file and line, the item above, the RFC section, and the fix. Do not claim an item passes without reading the code or running a test for it.
