# Code-review checklist: Agent2Agent (A2A)

Rules and examples: [A] `../reference/a2a.md` (items copied from its section 5). The SDK is pre-release.

- [ ] Agent Card at `/.well-known/agent-card.json`, required fields present, HTTPS interface URLs, no secrets, cache headers. [A §1](../reference/a2a.md#1-rules)
- [ ] `securitySchemes` match what the endpoint really enforces; no implicit/password flows. [A §1](../reference/a2a.md#1-rules)
- [ ] Every operation authenticated and authorized; results scoped to the caller; "not yours" is not distinguishable from "not found". [A §2](../reference/a2a.md#2-do--dont)
- [ ] `A2A-Version` required and checked; `VersionNotSupportedError` returned. [A §4.2](../reference/a2a.md#42-errors-named-by-the-spec-332)
- [ ] Task ids server-generated; terminal-state rules enforced; `contextId`/`taskId` mismatch rejected. [A §1](../reference/a2a.md#1-rules)
- [ ] `AUTH_REQUIRED` flow uses an out-of-band channel; no token passthrough to MCP servers or other agents. [A §3](../reference/a2a.md#3-a2a-and-mcp)
- [ ] Push webhooks: SSRF guard, authenticated callback, idempotent receiver, timeouts, backoff. [A §1](../reference/a2a.md#1-rules)
- [ ] Card signature verified against a trusted key store when signing is used; no `jku` from the card. [A §1](../reference/a2a.md#1-rules)
- [ ] Input sizes, media types and rate limits enforced; logs free of credentials. [A §2](../reference/a2a.md#2-do--dont)
- [ ] SDK version pinned (pre-release status acknowledged) and behaviour tested. [A §4.4](../reference/a2a.md#44-net-sdk)
