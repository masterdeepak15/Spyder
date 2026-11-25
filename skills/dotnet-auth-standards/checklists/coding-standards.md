# Code-review checklist: Coding standards

For reviewing C# / ASP.NET Core changes. Each item points to the reference section with the rule and example, and to the RFC or specification. Mark an item N/A only if the code does not touch that area. Short rules: see the matching file in `../reference/` (routing in `../SKILL.md`).

Reference files: [C] `../reference/coding-standards-details.md`.

## 10. Coding standards

Sources are Microsoft documentation pages named in [C] (C# identifier conventions, .NET coding conventions, Framework Design Guidelines, architectural principles, DI, exceptions, TAP, logging). Open/Closed, Liskov, Interface segregation, KISS, YAGNI and composition over inheritance are not defined on those pages; [C] says so.

- [ ] Names follow the casing table: PascalCase types and members, camelCase parameters, `_field`, `s_field`, `I` prefix, PascalCase constants, `Async` suffix on awaitable methods; an `.editorconfig` with `IDE1006` enforces it in the build. [C §1.1, §1.2, §1.6](../reference/coding-standards-details.md#11-casing-and-prefixes), Identifiers article, Framework Design Guidelines
- [ ] File-scoped namespaces, `using` directives outside the namespace, namespace shape Company.Product.Feature. [C §1.3](../reference/coding-standards-details.md#13-files-and-namespaces), .NET coding conventions
- [ ] Tests are named method, scenario, expected result; no magic strings; private logic tested through public members. [C §1.4](../reference/coding-standards-details.md#14-tests), Unit testing best practices
- [ ] JSON member naming is set once (default options write the C# name, `JsonSerializerOptions.Web` writes camelCase); URIs use one canonical case. [C §1.5](../reference/coding-standards-details.md#15-rest-and-json-naming), RFC 8259, RFC 3986
- [ ] State is private, invariants are enforced in methods, collections are exposed read-only; no public mutable fields or global mutable state. [C §2.1](../reference/coding-standards-details.md#21-encapsulation), Architectural principles, Property Design
- [ ] Each interface has a reason (second implementation, test double, boundary); abstract classes have protected constructors; no members added to shipped interfaces. [C §2.2](../reference/coding-standards-details.md#22-abstraction), Interface Design, Abstract Class Design
- [ ] Reuse is by composition; inheritance only for a real is-a; virtual and protected members are justified; sealing policy is consistent. [C §2.3, §2.4](../reference/coding-standards-details.md#23-inheritance-versus-composition), Virtual Members, Sealing
- [ ] Single responsibility: one reason to change per class; many constructor dependencies treated as a smell. [C §3.1](../reference/coding-standards-details.md#31-s-single-responsibility), Architectural principles, DI guidelines
- [ ] Open/Closed: variation that really grows is added as a new type, not by editing a switch. [C §3.2](../reference/coding-standards-details.md#32-o-openclosed), Martin (not a Microsoft page)
- [ ] Interface segregation: role-sized interfaces; no implementation throws `NotSupportedException` for an interface member. [C §3.3](../reference/coding-standards-details.md#33-i-interface-segregation), Martin, Framework Design Guidelines (Abstractions)
- [ ] Liskov: derived types keep the base contract; no mutable base shared by Square-like specializations. [C §3.4](../reference/coding-standards-details.md#34-l-liskov-substitution), Martin (not a Microsoft page)
- [ ] Dependency inversion: collaborators (clock, repositories, HTTP clients) injected through the constructor; no `new` of dependencies, no static access, no service locator. [C §3.5, §4.4](../reference/coding-standards-details.md#35-d-dependency-inversion), Architectural principles, DI guidelines
- [ ] No duplicated rules, no premature abstraction; duplication preferred to the wrong abstraction. [C §4.1](../reference/coding-standards-details.md#41-dry-kiss-yagni), Architectural principles
- [ ] DTOs and messages are records or immutable; structs only for small immutable single values. [C §4.2](../reference/coding-standards-details.md#42-immutability-records-and-structs), Records, Choosing Between Class and Struct
- [ ] Nullable reference types enabled; no `!` to silence warnings; public arguments validated. [C §4.3](../reference/coding-standards-details.md#43-nullable-reference-types), Nullable reference types
- [ ] DI lifetimes correct (no scoped service in a singleton), `ValidateScopes` and `ValidateOnBuild` on outside Development too, disposables not transient-registered. [C §4.4](../reference/coding-standards-details.md#44-dependency-injection), Service lifetimes, DI guidelines
- [ ] Specific exception types; no `catch (Exception)` below the top level; `throw;` not `throw ex;`; argument checks before async work; problem details at the API edge. [C §4.5](../reference/coding-standards-details.md#45-exceptions), Exception Throwing, Best practices for exceptions
- [ ] No `async void` (except event handlers), no `.Result` or `.Wait()`; `CancellationToken` accepted last and passed on. [C §4.6](../reference/coding-standards-details.md#46-asyncawait), Task-based asynchronous pattern, Asynchronous programming scenarios
- [ ] Logging uses message templates with named placeholders (no `$""`), no secrets or personal data, `[LoggerMessage]` on hot paths. [C §4.7](../reference/coding-standards-details.md#47-logging), Logging in .NET

## How to report

For each finding give: file and line, the item above, the RFC section, and the fix. Do not claim an item passes without reading the code or running a test for it.
