# General .NET coding standards for C# / ASP.NET Core
> Details file: full explanations, examples, per-section checklists and sourcing. The short must-follow rules are in [coding-standards.md](coding-standards.md); load that first. Load this file only for the section you need.

Scope: naming conventions, object-oriented principles, SOLID, and the basics that go with them (DRY, KISS, YAGNI, immutability and records, nullable reference types, dependency injection, exceptions, async/await, logging). Auth and protocol rules are in the other reference files; this file is the general layer under them.

**Sources.** Downloaded as data only into `_sources/coding/` from the public `dotnet/docs` repository (raw Markdown of the pages on learn.microsoft.com) and read for the statements below. Rules are tagged with the page title, never with a section number:

- **[Identifiers]** "C# identifier naming rules and conventions"; **[Conventions]** "Common C# code conventions" (page title ".NET Coding Conventions").
- **[FDG]** "Framework design guidelines" pages: Capitalization Conventions, General Naming Conventions, Names of Namespaces, Names of Classes, Structs, and Interfaces, Names of Type Members, Exception Throwing, Using Standard Exception Types, Interface Design, Abstract Class Design, Struct Design, Choosing Between Class and Struct, Property Design, Constructor Design, Designing for Extensibility (Unsealed Classes, Virtual Members, Protected Members, Sealing, Abstractions, Base Classes for Implementing Abstractions). **Caveat printed on those pages:** the content comes from the 2008 second edition of the book and "some of the information on this page may be out-of-date". They are written for library authors; apply them to public APIs and use judgment inside applications.
- **[Architecture]** "Architectural principles" (separation of concerns, encapsulation, dependency inversion, explicit dependencies, single responsibility, DRY).
- **[OOP]** "Overview of object oriented techniques in C#", Inheritance, Polymorphism.
- **[TAP]** "Task-based asynchronous pattern"; **[Async]** "Asynchronous programming scenarios".
- **[DI]** "Dependency injection guidelines", "Service lifetimes", "Dependency injection" overview (scope validation).
- **[Exceptions]** "Best practices for exceptions".
- **[Logging]** "Logging in .NET" overview and "High-performance logging".
- **[Records]** "Records" language reference; **[NRT]** "Nullable reference types".
- **[Tests]** "Unit testing best practices".
- RFC 3986 (URI case rules) and RFC 8259 (JSON) from `_sources/http/`.

**Not sourced.** The Microsoft pages read do **not** define Open/Closed, Liskov substitution, Interface segregation, KISS, YAGNI, "composition over inheritance" or "file name equals type name". Where this file covers them, the text says so and the statement is the author's recommendation or a widely used principle, not a Microsoft rule. Searching all downloaded pages for "SOLID", "Liskov", "open/closed" and "interface segregation" found no occurrence.

**(verified)** marks behaviour observed in `examples/CodingStandards` (net10.0, 0 warnings with warnings as errors, `dotnet run` prints `ALL PASS`) or in `examples/CodingStandards.Bad` (builds with the expected warnings; its run prints the two contract violations). Nothing in this file was tested on other frameworks or compilers.

---

## 1. Naming conventions

Conventions are not compiler rules ([Identifiers]). Enforce them with an `.editorconfig` so the build, not the reviewer, finds violations.

### 1.1 Casing and prefixes

| Element | Convention | Source |
|---|---|---|
| Namespaces, types (class, struct, record, delegate, enum), all public/protected members, methods, local functions, properties, events | `PascalCase` | [Identifiers], [FDG] Capitalization Conventions |
| Parameters, local variables | `camelCase` | [Identifiers] |
| Private and internal instance fields | `_camelCase` | [Identifiers] (runtime team style) |
| Private and internal static fields | `s_camelCase`; thread-static `t_camelCase` | [Identifiers]; the page says this is not the Visual Studio default and not part of the Framework Design Guidelines |
| Constants (fields and locals, any accessibility) | `PascalCase` | [Identifiers] |
| Interfaces | `IPascalCase` | [Identifiers], [FDG] Names of Classes, Structs, and Interfaces |
| Class-interface pair | names differ only by the `I` | [FDG] same page |
| Type parameters | `T`, or descriptive and prefixed `TSession` | [Identifiers], [FDG] same page |
| Attributes, exceptions, event args | suffix `Attribute`, `Exception`, `EventArgs` | [FDG] same page |
| Enums | singular noun; plural for `[Flags]`; no `Enum`/`Flags` suffix; no prefix on values | [FDG] same page |
| Positional record parameters | `PascalCase` (they become properties) | [Identifiers] |
| Primary constructor parameters on class/struct | `camelCase` | [Identifiers], [Conventions] |

Rules that do not depend on style: do not use two consecutive underscores (reserved for compiler-generated names); no Hungarian notation; no language-specific names such as `ToLong` (use `ToInt64`); avoid abbreviations except widely accepted ones; prefer clarity over brevity ([Identifiers], [FDG] General Naming Conventions).

Casing details from [FDG] Capitalization Conventions: acronyms longer than two letters are cased as words (`HtmlTag`, `Url`); two-letter acronyms stay upper in Pascal (`IOStream`) and lower when they begin a camelCase name (`ioStream`); closed compounds are one word (`Endpoint`, `Metadata`, `Callback`); `Id`, `Ok`, `Email`, `UserName`, `SignIn` and `LogOn` use the casing in that table. Names must not differ by case alone.

Members ([FDG] Names of Type Members): methods are verb phrases; properties are nouns or adjectives; collections are plural (`Orders`, not `OrderList`); Booleans are affirmative (`CanSeek`, not `CantSeek`), optional `Is`/`Has`/`Can`; events are verbs, present tense before and past tense after (`Closing`, `Closed`), not `Before`/`After`; no property that mirrors a `Get` method.

### 1.2 Async suffix

Methods that return an awaitable type end in `Async`; do not add it to synchronous methods; keep the old synchronous method when adding `MethodNameAsync`. The cancellation parameter is named `cancellationToken`. Task combinators such as `WhenAll` are exempt ([TAP]).

### 1.3 Files and namespaces

- Namespace shape `<Company>.(<Product>|<Technology>)[.<Feature>][.<Subnamespace>]`, PascalCase, dot-separated; stable product names; not org-chart names; no namespace and type with the same name; no generic type names such as `Node` or `Message` that collide with common ones ([FDG] Names of Namespaces).
- Use **file-scoped** namespaces (`namespace Example.Orders;`) and put `using` directives **outside** the namespace; the page shows how a `using` inside a namespace can silently bind to a different namespace after a dependency adds one ([Conventions]).
- **One type per file, file named after the type:** common practice, **not stated in the pages read**. Pick it, put it in `.editorconfig`/review rules, and apply it consistently.

### 1.4 Tests

Name a test as method, scenario, expected behaviour: for example `Add_QuantityZero_ThrowsArgumentOutOfRange` ([Tests]: "Name of the method being tested", "Scenario", "Expected behavior"). Use Arrange / Act / Assert, avoid magic strings (use named constants), and test private logic through the public method that calls it ([Tests]).

### 1.5 REST and JSON naming

Microsoft's naming pages cover .NET identifiers, not wire formats, so these are the checks that were made:

- **JSON member names:** RFC 8259 defines a name as a string and says nothing about casing. `System.Text.Json` with default options writes the C# name unchanged (`{"OrderId":1}`); `JsonSerializerOptions.Web` writes camelCase (`{"orderId":1}`) **(verified)**. Pick one wire convention (camelCase is the usual choice for JSON) and set it once; DTO properties stay `PascalCase` in C#. I did not test which options an ASP.NET Core host applies by default for your endpoints: check one response with curl.
- **URIs:** RFC 3986 section 6.2.2.1: scheme and host are case-insensitive, other components are assumed case-sensitive unless defined otherwise. So `/Orders` and `/orders` can be different resources. Choose one canonical form (lowercase, plural nouns, hyphens between words is the usual team choice; that choice is not an RFC rule) and see `api-design.md` section 1 for the rest (nouns, opaque ids, no secrets in URLs).
- **Enums on the wire:** strings, not numbers (`api-design.md` section 2).
- Header, query, and claim names are defined by their specifications (for example `scope`, `Retry-After`); do not "fix" their casing.

### 1.6 Enforcing it

Naming rules go in `.editorconfig` (`dotnet_naming_rule.*`), with `EnforceCodeStyleInBuild` in the project. Code analysis rules CA1715 (interface prefix), CA1707 (underscores) and CA1822 reported on the bad sample **(verified)**.

```ini
# examples/CodingStandards/.editorconfig (extract; first matching rule wins, put specific rules first)
dotnet_naming_style.underscore_camel.capitalization = camel_case
dotnet_naming_style.underscore_camel.required_prefix = _
dotnet_naming_symbols.private_fields.applicable_kinds = field
dotnet_naming_symbols.private_fields.applicable_accessibilities = private, internal
dotnet_naming_rule.private_fields_underscore.symbols = private_fields
dotnet_naming_rule.private_fields_underscore.style = underscore_camel
dotnet_naming_rule.private_fields_underscore.severity = warning

dotnet_diagnostic.IDE1006.severity = warning   # needed: without it the build printed no naming warnings
```

**(verified)** With the full file and `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>`, the bad sample produced IDE1006 warnings for: missing `I` prefix, missing `_`, missing `s_`, missing `Async` suffix, `max_lines` constant and `order_service` type; the good sample built with 0 warnings. Before adding `dotnet_diagnostic.IDE1006.severity` the same build showed no IDE1006 warnings.

---

## 2. Object-oriented principles

### 2.1 Encapsulation

Hide state; change it only through members that enforce the invariants ([Architecture] Encapsulation; [OOP] Encapsulation and Accessibility: default accessibility is `private`, limit it to what clients need). Mutable global state is "antithetical to encapsulation" ([Architecture]). Property rules ([FDG] Property Design): get-only when callers must not change it; no set-only properties; do not throw from getters; a get-only property of a mutable reference type can still be mutated, so expose `IReadOnlyList<T>`.

```csharp
public sealed class Basket
{
    private const int MaxLines = 3;
    private readonly List<OrderLine> _lines = [];

    public IReadOnlyList<OrderLine> Lines => _lines;      // read-only view

    public void Add(OrderLine line)                      // the only way in: invariant enforced here
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(line.Quantity);
        if (_lines.Count >= MaxLines) throw new InvalidOperationException($"A basket holds at most {MaxLines} lines.");
        _lines.Add(line);
    }
}
```

**(verified)** The fourth `Add` throws `InvalidOperationException`; `Lines` has type `IReadOnlyList<OrderLine>`. Note that `ParamName` of the argument exception is the caller's expression text, `"line.Quantity"` here, not `"Quantity"` **(verified)**; assign to a local named like the parameter if the exact name matters.

### 2.2 Abstraction

An abstraction is a contract without a full implementation, as an interface or abstract class ([FDG] Abstractions). Rules from [FDG]:

- Define an interface when unrelated types, including value types, need a common API, or when types already inherit from something else (Interface Design). Avoid empty marker interfaces; use an attribute (Interface Design).
- Do not add members to an interface that has shipped; create a new interface (Interface Design).
- Abstract classes: protected or internal constructor, never public; ship at least one concrete implementation (Abstract Class Design).
- "Do not provide abstractions unless they are tested by developing several concrete implementations and APIs consuming the abstractions" (Abstractions). In application code this is also the practical test against a pointless `IFoo` for every `Foo`: introduce an interface when a second implementation, a test double or a boundary needs it.

### 2.3 Inheritance versus composition

- Inheritance models "is a specialization of" and gives polymorphism through `virtual`/`abstract` and `override` ([OOP] Inheritance and Polymorphism). Fields cannot be virtual; a derived class overrides only `virtual`/`abstract` members ([OOP] Polymorphism).
- Costs ([FDG] Virtual Members, Protected Members): every virtual call can run arbitrary code, so virtual members are costly to design, test and version; do not make members virtual without a reason; public members should call a protected virtual member; treat protected members of unsealed classes as public for security and compatibility; avoid calling virtual members in a constructor (Constructor Design).
- Sealing ([FDG] Sealing, Unsealed Classes): the guidelines say not to seal without a good reason and that unsealed is the default for frameworks. That advice targets libraries. In application code nobody outside your team inherits, so sealing classes that were not designed for inheritance is a team decision: choose one rule and enforce it.
- **Prefer composition when you only want to reuse behaviour** (hold a collaborator and delegate to it) and use inheritance only for a real is-a relation that obeys section 3.4. "Composition over inheritance" is a widely used principle; the Microsoft pages read do not state it as a rule. The base-class pages do give the reasoning: base classes "add surface area and increase the depth of inheritance hierarchies" ([FDG] Base Classes for Implementing Abstractions).

```csharp
// Composition: add auditing by wrapping the reader role instead of deriving from a store.
public sealed class AuditedReader(IOrderReader inner, List<string> audit) : IOrderReader
{
    public async Task<Order?> FindAsync(OrderId id, CancellationToken cancellationToken)
    {
        var order = await inner.FindAsync(id, cancellationToken);
        audit.Add($"find {id.Value:N} found={order is not null}");
        return order;
    }
}
```

**(verified)** The decorator reads through to the inner store and records one audit entry.

### 2.4 Polymorphism

Callers depend on a base type or interface and each implementation supplies its own behaviour; the alternative is a growing `switch` on a type or kind (see 3.2). A member hidden with `new` is not an override ([OOP] Inheritance: `new` states that the member is not meant to override the base member, and the compiler warns when it is missing). Use `override` unless you really mean to hide.

---

## 3. SOLID

Source note: [Architecture] defines **single responsibility** and **dependency inversion** (and explicit dependencies, separation of concerns). **Open/Closed, Liskov substitution and Interface segregation** are not defined on any Microsoft page that was read; they are Robert C. Martin's principles and the explanations below are the author's, checked only by running the samples. Bad samples are in `examples/CodingStandards.Bad/Bad.cs`, good ones in `examples/CodingStandards/Good.cs`.

### 3.1 S: Single responsibility

"Objects should have only one responsibility and ... only one reason to change" ([Architecture]). A constructor with many injected dependencies "might be a sign" that a class has too many responsibilities ([DI] Design services for dependency injection).

```csharp
// Bad: persistence, e-mail and PDF rendering change for different reasons.
public class OrderManager { public void Save(string o) {...} public void SendEmail(string o) {...} public string RenderPdf(string o) {...} }

// Good: one reason to change each; the caller composes them.
public interface IOrderStore    { Task SaveAsync(Order order, CancellationToken cancellationToken); }
public interface IOrderNotifier { Task NotifyAsync(Order order, CancellationToken cancellationToken); }
```

### 3.2 O: Open/Closed

Add behaviour by adding a type, not by editing a tested method.

```csharp
// Bad: every new customer kind edits this switch (and its tests).
public static decimal Apply(CustomerKind kind, decimal subtotal) => kind switch
{
    CustomerKind.Regular => subtotal,
    CustomerKind.Vip => subtotal * 0.9m,
    _ => throw new NotSupportedException(),
};

// Good: new rule = new class; PriceCalculator is untouched.
public interface IDiscountRule { decimal Apply(decimal subtotal); }
public sealed class PercentDiscount(decimal percent) : IDiscountRule
{
    public decimal Apply(decimal subtotal) => subtotal - subtotal * percent / 100m;
}
public sealed class PriceCalculator(IDiscountRule rule) { public decimal Price(Order order) => rule.Apply(order.Total); }
```

**(verified)** `NoDiscount` gives 20, `PercentDiscount(10)` gives 18 through the same `PriceCalculator`. Do not apply this reflexively: a `switch` over a closed set that never grows is simpler (see KISS and YAGNI in section 4).

### 3.3 I: Interface segregation

Small interfaces per role, so a consumer depends only on what it uses. Related [FDG] rule: an abstraction with too many members "becomes difficult or even impossible to implement" (Abstractions).

```csharp
// Bad: a read-only type must implement writes it cannot honour.
public interface IFatStore { string Read(int id); void Write(int id, string v); void Delete(int id); }
public class ReadOnlyStore : IFatStore { /* Write and Delete throw NotSupportedException */ }

// Good: role interfaces; one class may implement several.
public interface IOrderReader { Task<Order?> FindAsync(OrderId id, CancellationToken cancellationToken); }
public interface IOrderWriter { Task SaveAsync(Order order, CancellationToken cancellationToken); }
```

**(verified)** In the bad sample `store.Write(...)` on `ReadOnlyStore` throws `NotSupportedException`; in the good sample a consumer that takes `IOrderReader` cannot call `SaveAsync` at compile time.

### 3.4 L: Liskov substitution

Code written against a base type must keep working with any derived type. A subclass that narrows what callers may do, throws `NotSupportedException` for an inherited member, or changes the meaning of a member breaks this.

```csharp
// Bad: Square "is a" MutableRectangle in geometry, but breaks the contract that Width and Height are independent.
public class MutableSquare : MutableRectangle
{
    public override double Width  { get => base.Width;  set { base.Width = value; base.Height = value; } }
    public override double Height { get => base.Height; set { base.Width = value; base.Height = value; } }
}
// r.Width = 5; r.Height = 4; r.Area  =>  16, the contract says 20.

// Good: no shared mutable base; both are immutable Shapes with a sound Area.
public abstract class Shape { public abstract double Area { get; } }
public sealed class Rectangle(double width, double height) : Shape { public override double Area => width * height; }
public sealed class Square(double side) : Shape { public override double Area => side * side; }
```

**(verified)** The bad sample printed `Rectangle contract: 5 x 4 = 20, got 16`; the good sample's shapes return 6 and 4. See also [FDG] Struct Design: "do not define mutable value types".

### 3.5 D: Dependency inversion

"The direction of dependency ... should be in the direction of abstraction, not implementation details" and classes should explicitly require their collaborators in the constructor ([Architecture] Dependency inversion, Explicit dependencies).

```csharp
// Bad: hidden dependency on the system clock; cannot be tested without waiting for midnight.
public class InvoiceNumber { public string Next() => $"INV-{DateTime.UtcNow:yyyyMMdd}"; }

// Good: the abstraction is injected.
public interface IClock { DateTimeOffset UtcNow { get; } }
public sealed class InvoiceStamper(IClock clock) { public string Stamp(OrderId id) => $"{id.Value:N}@{clock.UtcNow:O}"; }
```

.NET also has `TimeProvider` as a built-in clock abstraction; it was not examined for this file.

---

## 4. Related basics

### 4.1 DRY, KISS, YAGNI

- **DRY.** One authority per piece of behaviour ([Architecture] DRY). The same page warns: "Duplication is always preferable to coupling to the wrong abstraction", and two constants with equal values that mean different things stay separate.
- **KISS.** Not defined on a Microsoft page read. The coding conventions ask for "clarity and simplicity" and to "avoid overly complex and convoluted code logic" ([Conventions]).
- **YAGNI.** Not defined on a Microsoft page read. Related, sourced: no abstraction until several implementations or consumers exist ([FDG] Abstractions); extensibility is costly, "choose the least costly extensibility mechanism that meets the requirements" ([FDG] Designing for Extensibility).

### 4.2 Immutability, records and structs

- Records are "primarily intended for supporting immutable data models" and give value equality and `with` copies; positional `record class` and `readonly record struct` have init-only properties, a plain `record struct` has mutable ones; immutability is **shallow** and may not suit every case (EF Core entities, for example) ([Records]).
- Use records for DTOs, messages and value objects; use classes for entities with identity and behaviour.
- Structs only when the type is a single small value, under 16 bytes, immutable and not boxed often ([FDG] Choosing Between Class and Struct); implement `IEquatable<T>`; no parameterless-constructor tricks ([FDG] Struct Design).
- Use `required` properties to force initialization instead of constructor overloads ([Conventions]).

**(verified)** `line == new OrderLine("A-1", 2, 5m)` is true; `line with { Quantity = 3 }` leaves the original at 2.

### 4.3 Nullable reference types

All project templates enable the nullable context; older projects do not ([NRT]). In an aware context a `T` must not be assigned null, a `T?` must be checked before dereferencing, and `!` only silences the compiler ([NRT]). Keep it on, treat nullable warnings as errors in new code, annotate honestly (`Order?` for "may not exist"), validate public arguments with `ArgumentNullException.ThrowIfNull` ([Exceptions] lists the throw helpers), and do not use `!` to make a warning go away.

**(verified)** The bad sample produced `CS8602` (dereference of `string?`) and `CS8625` (null assigned to non-nullable property).

### 4.4 Dependency injection

From [DI] and [Architecture]:

- Constructor injection; no direct `new` of collaborators; no static or global state; no service locator (`GetService` where DI works) and no resolving a factory at runtime; keep services small.
- Lifetimes ([DI] Service lifetimes): do not inject a scoped service into a singleton (it behaves like a singleton); singletons must be thread safe; use `IServiceScopeFactory` to create a scope inside a singleton.
- Do not register the singleton pattern yourself; let the container own the lifetime.
- Turn on scope validation: the default host performs the checks (no scoped service from the root provider, none injected into a singleton) only in the Development environment ([DI] overview), so also enable `ValidateScopes` and `ValidateOnBuild` in tests or startup. The sample below uses both.
- Disposable services: do not register `IDisposable` as transient; do not resolve transient or scoped disposables from the root provider; the container disposes only what it created ([DI] guidelines).
- No `BuildServiceProvider` during registration; no async work in DI factories (deadlock risk) ([DI]).
- Configuration through the options pattern, not by putting data in the container ([DI]).

**(verified)** `BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true })` with `AddScoped<IClock>` and `AddSingleton<InvoiceStamper>` (which takes `IClock`) threw an `AggregateException` whose inner message was `Cannot consume scoped service 'IClock' from singleton 'InvoiceStamper'`; with `AddSingleton<IClock>` it resolved.

### 4.5 Exceptions

From [FDG] Exception Throwing, Using Standard Exception Types and [Exceptions]:

- Report failures by throwing; do not return error codes; do not use exceptions for normal control flow (use `TryParse`-style or a check first); throw rates around 100 per second noticeably affect performance ([FDG] Exception Throwing).
- Throw the most specific built-in type: `ArgumentException` family with `ParamName`, `InvalidOperationException` for a bad object state ([FDG] Using Standard Exception Types). Use the `ThrowIf*` helpers ([Exceptions]).
- Never throw `Exception`, `SystemException` or `ApplicationException`; never throw `NullReferenceException`, `IndexOutOfRangeException` or `StackOverflowException`; avoid catching `Exception` except in a top-level handler ([FDG] Using Standard Exception Types, [Conventions]).
- Custom exceptions end in `Exception` and provide the three standard constructors ([Exceptions]).
- Rethrow with `throw;`, never `throw ex;`; outside the catch block use `ExceptionDispatchInfo` ([Exceptions]). **(verified)** `throw;` kept the throwing method in the stack trace, `throw ex;` dropped it.
- Do not throw from `finally`, `Equals`, `GetHashCode`, `ToString`, static constructors ([FDG], [Exceptions]).
- In task-returning methods, validate arguments before the async part so argument exceptions are thrown synchronously; catch `OperationCanceledException`, not `TaskCanceledException` ([Exceptions]). **(verified)** A non-async wrapper threw `ArgumentNullException` at the call; an `async` method threw it only when awaited.
- At the API boundary, map exceptions to RFC 9457 Problem Details without internals: `http-rest.md` section 7.

### 4.6 Async/await

From [TAP], [Async], [Exceptions]:

- `async`/`await` all the way for I/O; never `.Result` or `.Wait()` (deadlocks, blocked threads). The `Task.Wait`/`Result` row in [Async] lists `await` as the replacement.
- `async void` only for event handlers: its exceptions cannot be caught outside, it is hard to test ([Async]).
- Accept a `CancellationToken cancellationToken` (last parameter) in every async I/O method and pass it on; cancellation surfaces as `OperationCanceledException` ([TAP]). **(verified)** a cancelled token produced `OperationCanceledException`.
- Return `Task`/`Task<T>`; no `out`/`ref` parameters on async methods ([TAP]).
- `ConfigureAwait(false)`: [Async] says to understand when to set it and links the ConfigureAwait FAQ; that FAQ was not read, so no rule is given here.
- Use caution with async lambdas inside LINQ: deferred execution can run the code at an unexpected time and blocking there can deadlock ([Async]).

### 4.7 Logging

From [Logging] and [Conventions]:

- Use message templates with **named** placeholders and pass values as arguments; do not use string interpolation or concatenation (it defeats structured logging; analyzer CA2254 flags it). **(verified)** The bad sample's `LogInformation($"Order {id} paid")` produced CA2254; the `[LoggerMessage]` version's state contained `OrderId`, `Total` and `{OriginalFormat}` as separate named values.
- Hot paths: `[LoggerMessage]` source-generated partial methods ([Logging] High-performance logging).
- Pick the level on purpose; `Trace` messages "may contain sensitive app data" and must not be enabled in production ([Logging]).
- Never log secrets, tokens, passwords or personal data (see the token rules in `oauth-oidc.md`).
- Name the placeholders (not numbers). The order of placeholders, not their names, binds the arguments, so keep them in sequence ([Logging]).

```csharp
public static partial class OrderLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Order {OrderId} paid, total {Total}")]
    public static partial void OrderPaid(ILogger logger, Guid orderId, decimal total);
}
```

---

## 5. Do and Don't

Do
- Enforce naming and style with `.editorconfig` and analyzers in the build.
- Inject abstractions you own; keep constructors to storing arguments ([FDG] Constructor Design).
- Make DTOs and messages records; keep entities encapsulated.
- Validate arguments at the top of public methods; throw specific exceptions.
- Pass `CancellationToken` through every async call chain.

Don't
- Don't inherit to reuse code; don't make members virtual "just in case".
- Don't create an interface per class without a second implementation, a test double or a boundary.
- Don't catch `Exception` to hide errors; don't `throw ex;`.
- Don't use `async void`, `.Result`, `.Wait()`.
- Don't use `!` to silence nullable warnings; don't build log messages with `$""`.
- Don't use the service locator or static access to services.

---

## 6. Checklist

- [ ] Names follow section 1.1 (casing, `_field`, `s_field`, `I` prefix, PascalCase constants, `Async` suffix); an `.editorconfig` enforces them in the build.
- [ ] Namespaces are file-scoped, `using` directives are outside, namespace names follow Company.Product.Feature.
- [ ] Test names state method, scenario and expected result.
- [ ] JSON naming is set once; URIs use one canonical case; no wire name derived from a C# accident.
- [ ] State is private; invariants are enforced in methods; collections are exposed read-only.
- [ ] Interfaces exist for a reason (second implementation, test double, boundary); abstract classes have protected constructors.
- [ ] Reuse is by composition; inheritance only for a real is-a; virtual members are justified; sealing policy is consistent.
- [ ] Each class has one reason to change; constructors do not take many unrelated dependencies.
- [ ] New behaviour is added by new types where the variation is real, not by editing switches.
- [ ] Subtypes honour the base contract; no `NotSupportedException` for inherited members; interfaces are role-sized.
- [ ] No `new` of collaborators, no static or global state, no service locator; time and randomness are injected.
- [ ] No duplicated rules; no premature abstraction.
- [ ] Data types are records or immutable; structs only for small immutable values.
- [ ] Nullable reference types on; no `!` used to hide warnings.
- [ ] DI lifetimes are right; scope validation and `ValidateOnBuild` enabled; no captive dependencies; disposables handled.
- [ ] Specific exception types; no `catch (Exception)` except at the top level; `throw;` for rethrow; arguments validated before async work.
- [ ] No `async void` (except event handlers), no `.Result`/`.Wait()`; `CancellationToken` flows through.
- [ ] Logging uses templates, not interpolation; no secrets or personal data in logs.

---

## 7. What was not covered

- Open/Closed, Liskov and Interface segregation, KISS, YAGNI, composition over inheritance and one-type-per-file are not defined on the Microsoft pages that were read; they are marked as such above and were not checked against Martin's or the Gang of Four's texts.
- The ConfigureAwait FAQ, the full runtime coding style (`dotnet/runtime` coding-style.md), `TimeProvider`, `IDisposable`/`IAsyncDisposable` implementation patterns, equality overloads, events and delegates design, parameter design, extension methods, the full Framework Design Guidelines (third edition was not read), and C# language-version features beyond the examples.
- Linux/macOS builds and analyzer behaviour on other SDK versions: all runs were on one Windows machine with .NET SDK 10.0.401.
