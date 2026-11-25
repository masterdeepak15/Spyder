# C# coding standards rules (C# / ASP.NET Core)

Must-follow rules only. Source pages, tables, bad/good code, `(verified)` notes and scope limits: [coding-standards-details.md](coding-standards-details.md) (section numbers match it). Samples: `examples/CodingStandards/` (good, `dotnet run`) and `examples/CodingStandards.Bad/`. Open/Closed, Liskov, Interface segregation, KISS, YAGNI, composition over inheritance and one-type-per-file are not defined on the Microsoft pages read: they are labelled principles or author recommendations, not Microsoft rules.

## 1. Naming ([§1](coding-standards-details.md#1-naming-conventions))
- `PascalCase`: namespaces, types, members, constants. `camelCase`: parameters, locals, primary-constructor parameters. Private/internal instance fields `_camelCase`; static `s_camelCase`; interfaces `IName`; type parameters `T` or `TName`. Suffixes `Attribute`, `Exception`, `EventArgs`; enums singular (plural for `[Flags]`).
- No double underscores, no Hungarian notation, no language-specific names (`ToInt64`, not `ToLong`); acronyms over two letters cased as words (`HtmlTag`); names must not differ by case only.
- Methods are verb phrases; properties nouns; collections plural; Booleans affirmative; events verbs (`Closing`, `Closed`).
- Awaitable methods end in `Async`, parameter `cancellationToken`; no `Async` on sync methods ([§1.2](coding-standards-details.md#12-async-suffix)).
- File-scoped namespaces; `using` outside the namespace; shape `Company.Product.Feature`; no type named like its namespace ([§1.3](coding-standards-details.md#13-files-and-namespaces)).
- Tests: method, scenario, expected result; no magic strings; test private logic via public members.
- Wire names: set JSON casing once (default options write the C# name; `JsonSerializerOptions.Web` writes camelCase); URIs one canonical case (RFC 3986: path is case-sensitive); enums as strings; never "fix" casing of spec-defined names (`scope`, `Retry-After`) ([§1.5](coding-standards-details.md#15-rest-and-json-naming)).
- Enforce in `.editorconfig` with `dotnet_diagnostic.IDE1006.severity = warning` plus `EnforceCodeStyleInBuild`; without both the build shows no naming warnings ([§1.6](coding-standards-details.md#16-enforcing-it)).

## 2. Object-oriented principles ([§2](coding-standards-details.md#2-object-oriented-principles))
- State is private; invariants are enforced in the members that change it; expose collections as `IReadOnlyList<T>`; no public mutable fields, no mutable global state, no set-only properties, no throwing getters.
- Interface only when a second implementation, test double or boundary needs it; no members added to a shipped interface; abstract classes get a protected constructor.
- Reuse by composition (hold and delegate); inheritance only for a real is-a that obeys Liskov. No `virtual` without a reason; no virtual calls in constructors; use `override`, not `new`. Pick one sealing rule for application code.

## 3. SOLID ([§3](coding-standards-details.md#3-solid))
- Single responsibility: one reason to change per class; many constructor dependencies are a smell.
- Open/Closed (Martin): variation that really grows becomes a new type, not an edited `switch`; a closed set that never grows stays a `switch`.
- Interface segregation (Martin): role-sized interfaces; no implementation throws `NotSupportedException` for an interface member.
- Liskov (Martin): derived types keep the base contract; no mutable base shared by Square-like types; no mutable value types.
- Dependency inversion: collaborators (clock, repositories, HTTP clients) injected by constructor; no `new` of collaborators, no static access, no hidden `DateTime.UtcNow` (inject a clock).

## 4. Basics ([§4](coding-standards-details.md#4-related-basics))
- DRY: one authority per behaviour; duplication beats coupling to the wrong abstraction. KISS/YAGNI: no abstraction before several implementations exist.
- Immutability: DTOs, messages and value objects are records (`with` copies; immutability is shallow); entities are classes. Structs only for small (under 16 bytes) immutable single values with `IEquatable<T>`. `required` for mandatory members ([§4.2](coding-standards-details.md#42-immutability-records-and-structs)).
- Nullable reference types on, nullable warnings as errors in new code; annotate honestly (`Order?`); `ArgumentNullException.ThrowIfNull` on public arguments; never `!` to hide a warning ([§4.3](coding-standards-details.md#43-nullable-reference-types)).
- DI: constructor injection; no service locator, no `BuildServiceProvider` during registration, no async in factories; no scoped service in a singleton (use `IServiceScopeFactory`); singletons thread safe; do not register `IDisposable` as transient; enable `ValidateScopes` and `ValidateOnBuild` outside Development too; configuration via options ([§4.4](coding-standards-details.md#44-dependency-injection)).
- Exceptions: throw, do not return error codes; most specific built-in type (`ArgumentException` family, `InvalidOperationException`) with `ThrowIf*` helpers; never throw `Exception`/`ApplicationException`/`NullReferenceException`; no `catch (Exception)` below the top-level handler; rethrow with `throw;` never `throw ex;`; never throw from `finally`, `Equals`, `GetHashCode`, `ToString`; custom exceptions end in `Exception`; validate arguments before the async part; catch `OperationCanceledException`; map to RFC 9457 Problem Details at the API edge, no internals ([§4.5](coding-standards-details.md#45-exceptions)).
- Async: `async`/`await` all the way; never `.Result` or `.Wait()`; `async void` only for event handlers; `CancellationToken` last parameter, passed through every call; no `out`/`ref` on async methods; careful with async lambdas in LINQ. No rule given for `ConfigureAwait` (FAQ not read) ([§4.6](coding-standards-details.md#46-asyncawait)).
- Logging: message templates with named placeholders, never `$""` or concatenation (CA2254); `[LoggerMessage]` on hot paths; no secrets, tokens or personal data; no `Trace`/`Debug` in production; log an exception once, at the boundary ([§4.7](coding-standards-details.md#47-logging)).

## Not covered
ConfigureAwait FAQ, `TimeProvider`, `IDisposable` patterns, events/delegates design, extension-method and parameter design, FDG third edition. FDG pages are the 2008 edition and written for library authors.

## Review
Per-area checklist: [../checklists/coding-standards.md](../checklists/coding-standards.md).
