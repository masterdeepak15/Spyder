# Code-review checklist: MCP servers and AI agents

For reviewing C# / ASP.NET Core changes. Each item points to the reference section with the rule and example, and to the RFC or specification. Mark an item N/A only if the code does not touch that area. Short rules: see the matching file in `../reference/` (routing in `../SKILL.md`).

Reference files: [M] `../reference/mcp-ai.md`.

## 8. MCP servers and AI agents

- [ ] Spec era (`2025-11-25` or `2026-07-28`) chosen and documented; `SessionMode` explicit; stateless preferred. [M §1](../reference/mcp-ai.md#1-spec-revisions), MCP spec
- [ ] Protected Resource Metadata served; its `resource` equals the audience and public URL. [M §3](../reference/mcp-ai.md#3-authorization-model), RFC 9728
- [ ] 401 carries `resource_metadata` (and `scope`); a bad token adds `invalid_token`; 403 carries `insufficient_scope`. [M §3](../reference/mcp-ai.md#3-authorization-model), RFC 6750, RFC 9728
- [ ] JwtBearer pins issuer, audience, algorithms and `ValidTypes`; no tokens in query strings. [M §4](../reference/mcp-ai.md#4-aspnet-core-wiring), RFC 8707, RFC 9068
- [ ] Per-tool authorization via `AddAuthorizationFilters()` and policies; ownership checks; `tools/list` hides forbidden tools; every policy named in `[Authorize(Policy)]` is registered (e.g. `tools-write`). [M §4, §5](../reference/mcp-ai.md#5-per-tool-authorization-and-scopes), MCP spec
- [ ] No token passthrough: downstream calls use a new token (client credentials or exchange); proxy servers have per-client consent. [M §6](../reference/mcp-ai.md#6-token-passthrough-and-confused-deputy), RFC 8693
- [ ] `Origin` validated; local HTTP on loopback and authenticated; stdio stdout reserved for the protocol. [M §2](../reference/mcp-ai.md#2-transports), MCP spec
- [ ] Tool descriptions static, outputs structured and capped, inputs validated beyond type, no shell with model input; URL-fetching tools SSRF-guarded. [M §7, §8](../reference/mcp-ai.md#8-input-validation), MCP Security Best Practices
- [ ] No secrets in source, logs or tool output; rate limiter per subject; audit logs tool, subject, outcome and denials (policy denials never reach the call-tool filter: log them elsewhere). [M §9, §10](../reference/mcp-ai.md#10-rate-limits-and-audit), MCP Security Best Practices
- [ ] Destructive tools need single-use, server-verified approval; annotations honest. [M §11](../reference/mcp-ai.md#11-human-approval), MCP spec
- [ ] Agent side: token per server with `resource`; refresh tokens encrypted and revocable; discovery URLs SSRF-guarded. [M §12](../reference/mcp-ai.md#12-agent-token-handling), RFC 8707, RFC 7009
- [ ] Anything from an Internet-Draft (OAuth 2.1, Client ID Metadata Documents) is labelled a draft; SDK version and spec revision recorded. [M §3, §15](../reference/mcp-ai.md#15-checklist), MCP spec

## How to report

For each finding give: file and line, the item above, the RFC section, and the fix. Do not claim an item passes without reading the code or running a test for it.
