# Code-review checklist: OpenAPI and JSON Schema 2020-12

Rules and examples: [O] `../reference/openapi-jsonschema.md` (items copied from its section 6).

- [ ] OpenAPI document generated (not hand-written), committed, diffed, breaking-change check in CI. [O §6](../reference/openapi-jsonschema.md#6-review-checklist)
- [ ] `securitySchemes` declared (oauth2 authorizationCode and/or http bearer JWT); no implicit/password flows. [O §3.1](../reference/openapi-jsonschema.md#31-security-scheme-and-per-operation-requirement)
- [ ] Each protected operation carries `security` with the scopes its policy enforces (one metadata source). [O §3.1](../reference/openapi-jsonschema.md#31-security-scheme-and-per-operation-requirement)
- [ ] 400/401/403/404 declared as `application/problem+json`; unique `operationId`s. [O §1](../reference/openapi-jsonschema.md#1-rules)
- [ ] Document parsed in a test with 0 errors and 0 warnings. [O §5](../reference/openapi-jsonschema.md#5-what-was-and-was-not-checked)
- [ ] Every published schema has `$schema` 2020-12; other dialects rejected or documented. [O §1](../reference/openapi-jsonschema.md#1-rules)
- [ ] Composed schemas use `unevaluatedProperties: false`; tool inputs are closed objects. [O §3.3](../reference/openapi-jsonschema.md#33-composition)
- [ ] No remote `$ref` dereferencing; unresolved `$ref` fails closed. [O §1](../reference/openapi-jsonschema.md#1-rules)
- [ ] Semantic limits (`maxLength`, `pattern`, ranges, `maxItems`) added to generated schemas; `format` not relied on. [O §3.2](../reference/openapi-jsonschema.md#32-generate-a-2020-12-schema-from-a-type-and-validate-arguments)
- [ ] Schema size, depth and body size bounded before validation. [O §4](../reference/openapi-jsonschema.md#4-net-validation-options)
- [ ] Validation errors return a short message plus JSON Pointer, never the instance. [O §3.2](../reference/openapi-jsonschema.md#32-generate-a-2020-12-schema-from-a-type-and-validate-arguments)
