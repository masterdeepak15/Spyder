# Agent2Agent (A2A) protocol guidelines for C# / ASP.NET Core

Scope: the Agent2Agent protocol as an HTTP service you expose or call: Agent Card, authentication schemes, tasks, push notifications, security, and how it relates to MCP. Token mechanics are in `oauth-oidc.md` and `tokens-jwt.md`; MCP in `mcp-ai.md`; webhooks in `webhooks-events.md`; TLS in `transport-tls.md`.

**Status of this file: reading only.** Everything below comes from the A2A specification text. No A2A code was written or run for it, and no A2A SDK was tested. Treat code-level advice as the author's recommendation, labelled as such.

Source (data only, fetched 2026-11-25): "Agent2Agent (A2A) Protocol Specification", https://a2a-protocol.org/latest/specification/ , page header "Latest Released Version 1.0.0" (previous: 0.3.0, 0.2.6, 0.1.0). GitHub releases of `a2aproject/A2A` list v1.0.1 (2026-05-28) and v1.0.0 (2026-03-12) as latest; the spec says patch numbers do not affect compatibility (3.6). Section numbers below refer to that page. Not read: the "What's New in v1.0" notes, the enterprise-features and discovery guide pages, the gRPC binding detail (section 10), extension and binding governance pages.

## 1. Rules

1. **Know the version.** The version is Major.Minor (`1.0`). Clients MUST send an `A2A-Version` header (or the query parameter `A2A-Version`) on every request; an empty value is read as 0.3. If the version is unsupported the agent MUST return `VersionNotSupportedError` (3.6). A client that needs 1.0 features should pin the version and not fall back silently (3.6.3).
2. **Publish an Agent Card.** A server MUST make one available (8.1), normally at `https://{host}/.well-known/agent-card.json` (8.2, IANA registration 14.3). Required fields: `name`, `description`, `supportedInterfaces`, `version`, `capabilities`, `defaultInputModes`, `defaultOutputModes`, `skills` (4.4.1). `supportedInterfaces` is an ordered list of `{url, protocolBinding, protocolVersion, tenant?}`; the first is preferred; bindings are `JSONRPC`, `GRPC`, `HTTP+JSON` (4.4.6). HTTP interface URLs MUST be absolute HTTPS in production.
3. **The card is public, so keep it free of secrets.** It SHOULD NOT contain credentials or internal details (14.3). Put privileged detail in the **extended Agent Card**: the operation MUST require authentication, is declared with `capabilities.extendedAgentCard: true`, and returns `UnsupportedOperationError` if not offered (13.3). Extended cards SHOULD NOT hold internal URLs or unmasked credentials.
4. **Declare auth in the card; do not invent it.** `securitySchemes` (a map) uses the OpenAPI 3.2 Security Scheme model; each scheme is exactly one of `apiKey`, `http` (e.g. Bearer), `oauth2`, `openIdConnect`, `mutualTLS` (4.5.1). OAuth flows in A2A 1.0: `authorizationCode` (with `pkceRequired`), `clientCredentials`, `deviceCode`; `implicit` and `password` are marked deprecated (4.5.7-4.5.10). `oauth2MetadataUrl` points at RFC 8414 metadata (4.5.4). In .NET use `JwtBearer` for the A2A endpoint and follow `oauth-oidc.md`: authorization code + PKCE, client credentials for agent-to-agent.
5. **Authenticate every request; credentials are out of band.** Servers MUST authenticate every request (7.4); clients discover schemes from the card, get credentials out of band and send them with every request (7.3). The protocol carries no identity inside A2A messages (7). Return the binding's normal 401/403 with a challenge (7.4). Production MUST use TLS (HTTPS for HTTP bindings); TLS 1.3 recommended, clients SHOULD validate the server certificate (7.1, 7.2, 13.4).
6. **Authorize every operation and scope the results.** Check authorization on every request, before any query that could reveal whether a resource exists. `ListTasks` returns only tasks the caller may see, even with no `contextId` filter; `GetTask`, `CancelTask`, `SubscribeToTask` and push-config operations MUST verify access (13.1). Authorization models (user, role, project, tenant) are yours to define and document. A per-task owner check is the minimum.
7. **Treat tasks as the unit of work.** Server generates `taskId` (clients cannot choose it); a message with a `taskId` MUST reference an existing task or get `TaskNotFoundError`; a `contextId`/`taskId` mismatch MUST be rejected (3.4.2, 3.4.3). States: `SUBMITTED`, `WORKING`, `COMPLETED`, `FAILED`, `CANCELED`, `REJECTED` (terminal), `INPUT_REQUIRED`, `AUTH_REQUIRED` (interrupted) (4.1.3). Messages to terminal tasks get `UnsupportedOperationError`. Duplicate `SendMessage` may be detected via `messageId` (3.3.1, MAY).
8. **`AUTH_REQUIRED` is not authorization.** Moving a task to it does not authorize anything; credentials SHOULD arrive out of band over HTTPS; a credential obtained while in that state MUST NOT be assumed to cover later messages unless you define it (7.6.1-7.6.4). Never pass a user's token through a chain of agents in-band; if you must, bind it to the originating agent (7.6.3).
9. **Streaming and push.** Streaming (SSE) needs `capabilities.streaming`; the stream begins with the Task, then updates, and ends at a terminal state (3.5.2). Push notifications need `capabilities.pushNotifications`; the webhook receiver MUST validate the sender with the credentials from `PushNotificationConfig.authentication` and that the task id matches one it created, answer 2xx, and handle duplicates idempotently (13.2, 4.3.3). Webhook senders SHOULD block SSRF targets (private, loopback, link-local ranges), time out in 10-30 s and back off on retries (13.2). Reuse `webhooks-events.md` section on SSRF and idempotent receivers.
10. **Sign the card when it matters.** Signing is optional (MAY): JWS (RFC 7515) over the card canonicalised with JCS (RFC 8785), `signatures` excluded (8.4). Verifiers MUST NOT use expired or revoked keys; SHOULD verify at least one signature before trusting a card. The spec lets the verifier fetch the key via `kid` and `jku` **or from a trusted key store**: prefer the trusted store; do not follow a `jku` from an unauthenticated card (RFC 8725, `tokens-jwt.md`).
11. **Cache the card with HTTP caching.** Server: `Cache-Control: max-age` and an `ETag`; client: honour RFC 9111 and send conditional requests (8.6).
12. **Validate input and bound it.** Validate all parameters, limit message and file sizes, reject unexpected media types, rate-limit all operations, log auth failures without secrets (13.4). Parts carry file bytes or URLs: never fetch a URL from a Part without the SSRF guard in `webhooks-events.md`.

## 2. Do / Don't

| Do | Don't |
|---|---|
| Send `A2A-Version: 1.0` and handle `VersionNotSupportedError` | Rely on the 0.3 default for an empty header |
| Authenticate with a scheme declared in the card; validate tokens as in `tokens-jwt.md` | Accept anonymous task creation because "it is only agents" |
| Check task ownership on Get, List, Cancel, Subscribe, push config | Return all tasks when `contextId` is missing |
| Put privileged skills in the extended card behind auth | Put internal URLs or keys in the public card |
| Verify the card signature against a trusted key store | Fetch keys from a `jku` URL the card names |
| Validate webhook URLs against SSRF and authenticate callbacks | Post task data to any URL a client supplies |
| Treat `AUTH_REQUIRED` as "ask out of band" | Treat a state change as granting a permission |

## 3. A2A and MCP

Spec Appendix B: complementary, not competing. **MCP** connects a model or agent to tools, APIs and data (an agent uses a capability). **A2A** lets independent, opaque agents discover each other and delegate tasks as peers. Typical stack: an A2A server agent receives a task and uses MCP servers behind it to do the work. In .NET this means two separate endpoints with separate Authorization: the A2A endpoint authenticates calling agents (card schemes); the MCP server authenticates its client with `mcp-ai.md` rules (audience-bound token, no passthrough). **Never forward the token received on the A2A endpoint to an MCP server or another agent**; get a token for that audience (`oauth-oidc.md`, RFC 8693 / client credentials). Differences you will notice: MCP tool calls are synchronous requests with schemas; A2A has long-running tasks with states, streaming and webhooks; MCP uses JSON-RPC 2.0 over Streamable HTTP, A2A offers JSON-RPC, gRPC and HTTP+JSON bindings (this last point: A2A section 9-11; MCP transport in `mcp-ai.md`).

## 4. Details

### 4.1 HTTP+JSON binding paths (section 11.3)
`POST /message:send`, `POST /message:stream` (SSE), `GET /tasks/{id}`, `GET /tasks`, `POST /tasks/{id}:cancel`, `POST /tasks/{id}:subscribe`, `POST|GET|DELETE /tasks/{id}/pushNotificationConfigs[/{configId}]`, `GET /extendedAgentCard`. Media type `application/a2a+json` (14.1.1). JSON-RPC binding: one endpoint, methods `SendMessage`, `SendStreamingMessage`, `GetTask`, `ListTasks`, `CancelTask`, `SubscribeToTask`, `GetExtendedAgentCard` and push-config methods (9.4). Service parameters travel as headers (`A2A-Version`, `A2A-Extensions`).

### 4.2 Errors named by the spec (3.3.2)
`TaskNotFoundError`, `TaskNotCancelableError`, `PushNotificationNotSupportedError`, `UnsupportedOperationError`, `ContentTypeNotSupportedError`, `InvalidAgentResponseError`, `ExtendedAgentCardNotConfiguredError`, `ExtensionSupportRequiredError`, `VersionNotSupportedError`. Map each to the binding's status code per section 5.4 (not read in detail); never put internals in the message.

### 4.3 Sketch (author's recommendation, not compiled)
```csharp
// Serve the card; allow caching; keep it free of secrets.
app.MapGet("/.well-known/agent-card.json", (HttpContext ctx) =>
{
    ctx.Response.Headers.CacheControl = "public, max-age=300";
    ctx.Response.Headers.ETag = "\"" + cardVersion + "\"";
    return Results.Json(card);                       // card: your record with the 4.4.1 fields
}).AllowAnonymous();

// Task endpoints: authenticated, owner-scoped.
app.MapGet("/tasks/{id}", async (string id, ClaimsPrincipal user, ITaskStore store) =>
    await store.FindAsync(id, ownerSub: user.FindFirstValue("sub")!) is { } t ? Results.Ok(t) : Results.NotFound())
   .RequireAuthorization("a2a");                     // 404 for "not yours" too: no existence leak (13.1)
```
Property-name and enum-casing of the wire JSON (`TASK_STATE_*`, camelCase fields) must follow section 5.5-5.6 and the JSON Schema/proto files of the spec repository; check them before writing models.

### 4.4 .NET SDK
NuGet lists `A2A` and `A2A.AspNetCore`; at time of checking (2026-11-25) only pre-release versions exist (latest listed `1.0.0-preview4`). API and spec conformance **not checked**; pin versions and test against the card/task rules above.

### 4.5 Spec observations
- Section 14.3 mentions `AgentCard.security`, the data model (4.4.1) names the field `securityRequirements`. Use the data-model name and check your SDK.
- Section 14.2.1 example shows `A2A-Version: 0.3` in a 1.0 document; the normative rule is in 3.6.

## 5. Review checklist
- [ ] Agent Card at `/.well-known/agent-card.json`, required fields present, HTTPS interface URLs, no secrets, cache headers.
- [ ] `securitySchemes` match what the endpoint really enforces; no implicit/password flows.
- [ ] Every operation authenticated and authorized; results scoped to the caller; "not yours" is not distinguishable from "not found".
- [ ] `A2A-Version` required and checked; `VersionNotSupportedError` returned.
- [ ] Task ids server-generated; terminal-state rules enforced; `contextId`/`taskId` mismatch rejected.
- [ ] `AUTH_REQUIRED` flow uses an out-of-band channel; no token passthrough to MCP servers or other agents.
- [ ] Push webhooks: SSRF guard, authenticated callback, idempotent receiver, timeouts, backoff.
- [ ] Card signature verified against a trusted key store when signing is used; no `jku` from the card.
- [ ] Input sizes, media types and rate limits enforced; logs free of credentials.
- [ ] SDK version pinned (pre-release status acknowledged) and behaviour tested.
