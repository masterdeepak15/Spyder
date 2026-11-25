# OpenAPI and JSON Schema 2020-12 guidelines for C# / ASP.NET Core

Scope: the OpenAPI document ASP.NET Core generates, how to declare OAuth2 / JWT bearer security in it, and JSON Schema 2020-12 as used by OpenAPI 3.1 and MCP tool schemas. Resource design, versioning and Problem Details are in `api-design.md` and `http-rest.md`; MCP tools in `mcp-ai.md`.

Tested code: `examples/OpenApiSample/` (net10.0, `Microsoft.AspNetCore.OpenApi` 10.0.15, `JwtBearer` 10.0.15, `JsonSchema.Net` 9.7.0). Run `dotnet build` (0 warnings, 0 errors, warnings are errors) then `dotnet run -- selftest` (ALL PASSED, 31 checks). Files: `Api.cs` (API, security transformers), `JsonSchemaSnippets.cs` (generate and validate), `SelfTest.cs`.

Sources (read as data): OpenAPI 3.1.2 and 3.2.0 HTML (spec.openapis.org), JSON Schema 2020-12 Core and Validation (json-schema.org/draft/2020-12/), MCP `basic` 2026-07-28 and `server/tools`. Details of what was and was not checked are in section 5.

## 1. Rules

### OpenAPI document
1. Generate it, do not hand-write it: `AddOpenApi()` + `MapOpenApi()` (`/openapi/v1.json`). The generated document was `"openapi": "3.1.1"` (Microsoft.OpenApi 2.13.0). OpenAPI 3.1 and 3.2 use JSON Schema 2020-12 as Schema Object dialect (OAS 3.1.2 section 4.8.24.5); `$schema` in a schema resource root overrides the document default (`jsonSchemaDialect`).
2. Declare every scheme under `components.securitySchemes`, with a **document transformer**. Types are `apiKey`, `http`, `mutualTLS`, `oauth2`, `openIdConnect` (OAS 3.1.2, 4.8.27). For JWT access tokens: `http` + `scheme: bearer` + `bearerFormat: JWT` (documentation hint only) and an `oauth2` scheme with `authorizationCode` flow (PKCE; the spec itself recommends it over implicit). Never declare implicit or password flows.
3. Attach requirements per operation with an **operation transformer** driven by endpoint metadata, not by hand per route. In a Security Requirement, the value for `oauth2`/`openIdConnect` is the list of required **scope** names (may be empty); for other types it is `[]`.
4. Semantics of the `security` array (OAS 3.1.2, 4.8.30): entries are **alternatives** (one suffices); several schemes inside **one** entry must **all** be satisfied; `{}` means anonymous access is allowed; an operation-level `security: []` removes the top-level requirement. Do not put `oauth2` and `bearer` in one entry unless the client must send both.
5. The document describes; the pipeline enforces. The scopes in the document must match what the policy enforces. The sample writes each scope twice (policy name and `RequiredScopes` metadata), so they can drift; the selftest catches that by exercising 401/403/201 against the scopes the document declares. In a larger API derive both from one constant or one extension method.
6. List error responses (`401`, `403`, `404`, `400`) as `application/problem+json` (`ProducesProblem`). Give each operation a unique `WithName` (becomes `operationId`; used by client generators).
7. Commit the generated document, diff it in pull requests and fail CI on breaking changes. Publish it only if the route list is public information (it reveals every route). Keep example hosts, never real tenant URLs, in committed documents.
8. The document's `authorizationUrl`/`tokenUrl` are documentation for client developers. The API itself validates tokens as in `tokens-jwt.md` (issuer, audience, `at+jwt`, algorithm allow-list, `ClockSkew`).
9. Validate the output in a test: parse with `OpenApiDocument.Parse(json, "json")` and require 0 errors and 0 warnings in the diagnostic, plus `document.Validate(ValidationRuleSet.GetDefaultRuleSet())` empty (both checked in the sample).

### JSON Schema 2020-12
10. **Dialect.** Put `"$schema": "https://json-schema.org/draft/2020-12/schema"` in the root of every schema you publish. Without it Core says behavior is implementation-defined (Core 8.1.1); MCP defines 2020-12 as the default and says implementations MUST support it and handle unsupported dialects with an error (MCP basic, "Schema Dialect" and "Implementation Requirements"). Reject other `$schema` values explicitly instead of validating them as 2020-12 (`SchemaCheck.Compile`).
11. **`$defs` + `$ref`** for reuse: definitions live under `$defs` (not `definitions`), referenced as `"$ref": "#/$defs/name"`. In 2020-12 `$ref` may have sibling keywords.
12. **Never dereference remote `$ref`.** MCP 2026-07-28: implementations MUST NOT automatically dereference `$ref` to a network URI; an opt-in mode MUST be off by default and allowlisted. A schema with an unresolved external `$ref` is rejected, not treated as permissive. Give the validator a registry whose fetch always throws (sample: verified).
13. **`unevaluatedProperties: false`, not `additionalProperties: false`, when composing** with `allOf`/`$ref`/`if`/`then`. `additionalProperties` only sees sibling `properties`, so properties declared inside `allOf` are rejected; `unevaluatedProperties` sees annotations from the whole in-place applicator tree (Core section 11). Both outcomes are tested in the sample.
14. **Closed objects for tool inputs.** A tool with no parameters: `{"type":"object","additionalProperties":false}` (MCP tools spec, "recommended"). Otherwise list `required` and close the object, so unknown arguments fail.
15. **`format` is an annotation** by default in 2020-12 (Validation 7.2.1, Format-Annotation vocabulary). `"format":"email"` does not reject `"not-an-email"` with default settings (observed with JsonSchema.Net 9.7.0). Add `pattern`, `maxLength` or your own parsing for anything security relevant.
16. **Generated schemas are type checks only.** `JsonSchemaExporter` (`System.Text.Json.Schema`) from your C# types is a good start. Add `minLength`, `maxLength`, `pattern`, `minimum`, `maximum`, `maxItems`, `enum` where the type cannot say it (sample: `semantic` schema).
17. **Resource limits.** MCP 2026-07-28 says implementations SHOULD bound composition keywords (`anyOf`, `oneOf`, `allOf`, `if/then/else`) and `$defs` by depth, subschema count or time budget. Cap request body size and schema size before validating; never compile a schema taken from an untrusted peer without these limits.
18. Validation failure to the caller: JSON-RPC invalid-params or Problem Details 400 with a short message and the JSON Pointer of the failing location. Never echo the instance back.

## 2. Do / Don't

| Do | Don't |
|---|---|
| Generate the document, add schemes and per-operation requirements with transformers | Hand-edit the generated JSON, or write scopes in two places |
| `authorizationCode` flow (PKCE) in the document | Declare implicit or password flows |
| `unevaluatedProperties: false` on composed schemas | `additionalProperties: false` next to `allOf`/`$ref` |
| `$schema` 2020-12 at every root; reject other dialects | Assume the default, or accept draft-07 silently |
| `JsonSerializerOptions` without `JsonSerializerDefaults.Web` for schema export (see 3.2) | Export a schema with `NumberHandling.AllowReadingFromString` and expect integers to be integers |
| Fail closed on a schema that does not resolve | Treat "could not resolve `$ref`" as valid |
| Add semantic keywords (`maxLength`, `pattern`, ranges) | Rely on `format` to reject bad input |

## 3. Code (from `examples/OpenApiSample`, all compiled and run)

### 3.1 Security scheme and per-operation requirement
```csharp
builder.Services.AddOpenApi(o =>
{
    o.AddDocumentTransformer<SecuritySchemeTransformer>();   // components.securitySchemes
    o.AddOperationTransformer<OperationSecurityTransformer>(); // operation.security from metadata
});
// in the transformer:
document.Components.SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>
{
    ["oauth2"] = new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.OAuth2,
        Flows = new OpenApiOAuthFlows { AuthorizationCode = new OpenApiOAuthFlow {
            AuthorizationUrl = new Uri("https://auth.example.com/authorize"),
            TokenUrl = new Uri("https://auth.example.com/token"),
            Scopes = new Dictionary<string, string> { ["items:read"] = "Read items", ["items:write"] = "Create items" } } },
    },
    ["bearer"] = new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT" },
};
operation.Security.Add(new OpenApiSecurityRequirement
{
    [new OpenApiSecuritySchemeReference("oauth2", context.Document)] = [.. scopes.Scopes],
});
```
Endpoint side: `.RequireAuthorization("items:write").WithMetadata(new RequiredScopes("items:write"))`.

### 3.2 Generate a 2020-12 schema from a type and validate arguments
```csharp
var options = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,   // -> additionalProperties:false
    TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
};
var schema = JsonSchemaExporter.GetJsonSchemaAsNode(options, typeof(SearchArgs),
    new JsonSchemaExporterOptions { TreatNullObliviousAsNonNullable = true }).AsObject();
schema["$schema"] = "https://json-schema.org/draft/2020-12/schema";
```
Observed output for `record SearchArgs(string Query, int Limit = 10)`:
`{"type":"object","properties":{"query":{"type":"string"},"limit":{"type":"integer","default":10}},"required":["query"],"additionalProperties":false,"$schema":"https://json-schema.org/draft/2020-12/schema"}`.
With `JsonSerializerDefaults.Web` the first run gave `"type":["object","null"]` for the root and `"type":["string","integer"]` for `limit` (so `"5"` passed validation); the options above fix both. Selftest checks wrong type, missing required and unknown property are rejected.

```csharp
var options = new BuildOptions { SchemaRegistry = new SchemaRegistry {
    Fetch = (uri, _) => throw new InvalidOperationException($"Remote $ref not allowed: {uri}") } };
var compiled = JsonSchema.FromText(schemaJson, options);             // Json.Schema (JsonSchema.Net 9.7.0)
bool ok = compiled.Evaluate(JsonDocument.Parse(instance).RootElement).IsValid;
```

### 3.3 Composition
```json
{ "$schema": "https://json-schema.org/draft/2020-12/schema",
  "allOf": [ { "type": "object", "properties": { "id": { "type": "string" } }, "required": ["id"] } ],
  "properties": { "name": { "type": "string" } },
  "unevaluatedProperties": false }
```
`{"id":"1","name":"n"}` is valid; `{"id":"1","name":"n","x":1}` is invalid. With `additionalProperties: false` instead, the first instance is invalid (tested).

## 4. .NET validation options

| Option | Status in this skill |
|---|---|
| `System.Text.Json.Schema.JsonSchemaExporter` (generate from types, built into net10.0) | Run: output above |
| `JsonSchema.Net` 9.7.0 (namespace `Json.Schema`, 2020-12 support, `SchemaRegistry.Fetch` hook) | Run: 31-check selftest; the API of 9.x differs from 4.x-8.x (`Fetch` takes `(Uri, SchemaRegistry)`, `Evaluate` takes `JsonElement`) |
| `Corvus.Json.Validator` 5.8.12, `NJsonSchema` | Exist on NuGet (Corvus version checked); **not run** here, no claims about their dialect support |
| Validation inside the MCP C# SDK | **Not checked** here; see `mcp-ai.md` (its schema is generated, treat it as a type check) |

Pick one validator, pin the version, and keep the "type check only" rule 16 in mind. JSON Schema validates shape; business rules and authorization still run in code.

## 5. What was and was not checked

- Read: OAS 3.1.2 and 3.2.0 sections on Security Scheme Object, Security Requirement Object, Schema dialects; JSON Schema Core 8.1.1 (`$schema`), 11 (unevaluated vocabulary), Validation 7.2.1 (format annotation); MCP basic 2026-07-28 ("$ref Resolution", "Composition-Keyword Resource Use") and `tools` (`inputSchema`).
- Latest OpenAPI release on GitHub at time of writing: 3.2.1 (2026-09-10); ASP.NET Core 10's generator emitted 3.1.1. Check your client generator before moving a contract to 3.2.
- Not read in full: the remaining OpenAPI sections, JSON Schema `dependentSchemas`, `$dynamicRef`, vocabularies other than the ones cited, output formats (Core 12).
- Not tested: JsonSchema.Net behaviour of `format` with the Format-Assertion vocabulary enabled; Swagger UI / Scalar UI; operation transformer when the `Document` is null in the context (the sample passes it as is, compiled with nullable enabled).

## 6. Review checklist
- [ ] OpenAPI document generated (not hand-written), committed, diffed, breaking-change check in CI.
- [ ] `securitySchemes` declared (oauth2 authorizationCode and/or http bearer JWT); no implicit/password flows.
- [ ] Each protected operation carries `security` with the scopes its policy enforces (one metadata source).
- [ ] 400/401/403/404 declared as `application/problem+json`; unique `operationId`s.
- [ ] Document parsed in a test with 0 errors and 0 warnings.
- [ ] Every published schema has `$schema` 2020-12; other dialects rejected or documented.
- [ ] Composed schemas use `unevaluatedProperties: false`; tool inputs are closed objects.
- [ ] No remote `$ref` dereferencing; unresolved `$ref` fails closed.
- [ ] Semantic limits (`maxLength`, `pattern`, ranges, `maxItems`) added to generated schemas; `format` not relied on.
- [ ] Schema size, depth and body size bounded before validation.
- [ ] Validation errors return a short message plus JSON Pointer, never the instance.
