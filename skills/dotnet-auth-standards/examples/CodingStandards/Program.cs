using System.Text.Json;
using Example.Orders;
using Microsoft.Extensions.DependencyInjection;

int failures = 0;

void Check(string name, bool ok)
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name}");
    if (!ok)
    {
        failures++;
    }
}

// Records: value equality, with-expression copies, original untouched.
var line = new OrderLine("A-1", 2, 5m);
var copy = line with { Quantity = 3 };
Check("record value equality", line == new OrderLine("A-1", 2, 5m));
Check("with-expression copies, original unchanged", copy.Quantity == 3 && line.Quantity == 2);

// Encapsulation: invariant enforced by the method; callers cannot add through the read-only view.
var basket = new Basket();
basket.Add(line);
basket.Add(line);
basket.Add(line);
bool threw = false;
try { basket.Add(line); } catch (InvalidOperationException) { threw = true; }
Check("Basket rejects 4th line", threw);
Check("Lines is exposed as IReadOnlyList (no Add)", typeof(Basket).GetProperty("Lines")!.PropertyType == typeof(IReadOnlyList<OrderLine>));
bool argThrew = false;
try { basket.Add(new OrderLine("B", 0, 1m)); } catch (ArgumentOutOfRangeException ex) { argThrew = ex.ParamName == "line.Quantity"; }
Check("ThrowIfNegativeOrZero: ParamName is the caller expression text (line.Quantity)", argThrew);

// OCP/DIP: new rule without editing the calculator.
var order = new Order(new OrderId(Guid.NewGuid()), "Ada", [line, line]);
Check("NoDiscount", new PriceCalculator(new NoDiscount()).Price(order) == 20m);
Check("PercentDiscount (added without editing PriceCalculator)", new PriceCalculator(new PercentDiscount(10m)).Price(order) == 18m);

// LSP: any Shape substitutes with a sound Area.
Shape[] shapes = [new Rectangle(2, 3), new Square(2)];
Check("Shape substitutes", shapes[0].Area == 6 && shapes[1].Area == 4);

// ISP + composition: decorator wraps the reader role only.
var store = new InMemoryOrders();
await store.SaveAsync(order, CancellationToken.None);
var audit = new List<string>();
var reader = new AuditedReader(store, audit);
var found = await reader.FindAsync(order.Id, CancellationToken.None);
Check("decorator reads through and audits", found == order && audit.Count == 1);

// Async: argument validation timing.
bool syncThrow = false;
try { _ = Guards.LengthAsync(null!, CancellationToken.None); } catch (ArgumentNullException) { syncThrow = true; }
Check("non-async wrapper throws ArgumentNullException synchronously", syncThrow);
Task<int> lazy = Guards.LengthLazyAsync(null!);
bool awaitedThrow = false;
try { await lazy; } catch (ArgumentNullException) { awaitedThrow = true; }
Check("async method throws only when awaited", awaitedThrow);
using var cts = new CancellationTokenSource();
await cts.CancelAsync();
bool canceled = false;
try { await Guards.LengthAsync("x", cts.Token); } catch (OperationCanceledException) { canceled = true; }
Check("cancellation surfaces as OperationCanceledException", canceled);

// Exceptions: throw; keeps the original frame, throw ex; restarts the stack trace.
string Trace(Action<Action> rethrower)
{
    try { rethrower(Guards.Thrower); } catch (InvalidOperationException ex) { return ex.StackTrace ?? ""; }
    return "";
}
Check("'throw;' keeps Thrower in the stack trace", Trace(Guards.RethrowCorrectly).Contains("Thrower"));
Check("'throw ex;' loses Thrower from the stack trace", !Trace(Guards.RethrowBadly).Contains("Thrower"));

// DI: scope validation catches a singleton that captures a scoped service.
var services = new ServiceCollection();
services.AddScoped<IClock, SystemClock>();
services.AddSingleton<InvoiceStamper>();
bool captive = false;
try
{
    using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    _ = provider.GetRequiredService<InvoiceStamper>();
}
catch (Exception ex) when (ex is AggregateException or InvalidOperationException && ex.ToString().Contains("Cannot consume scoped service", StringComparison.Ordinal))
{
    captive = true;
}
Check("ValidateScopes/ValidateOnBuild reject singleton -> scoped", captive);

var ok = new ServiceCollection();
ok.AddSingleton<IClock, SystemClock>();
ok.AddSingleton<InvoiceStamper>();
using (var provider = ok.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }))
{
    Check("singleton -> singleton resolves", provider.GetRequiredService<InvoiceStamper>().Stamp(order.Id).Contains('@'));
}

// Logging: the message template stays a template; values arrive as named properties.
var capture = new CapturingLogger();
OrderLog.OrderPaid(capture, order.Id.Value, 20m);
Check("log template placeholders become named properties",
    capture.Last is { } s && s.Any(p => p.Key == "OrderId") && s.Any(p => p.Key == "Total") && s.Any(p => p.Key == "{OriginalFormat}"));

// JSON naming: System.Text.Json default is PascalCase, the Web defaults are camelCase.
string defaultJson = JsonSerializer.Serialize(new { OrderId = 1 });
string webJson = JsonSerializer.Serialize(new { OrderId = 1 }, JsonSerializerOptions.Web);
Check("default serializer keeps PascalCase", defaultJson == "{\"OrderId\":1}");
Check("JsonSerializerOptions.Web writes camelCase", webJson == "{\"orderId\":1}");

Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILED");
return failures == 0 ? 0 : 1;

sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger
{
    public IReadOnlyList<KeyValuePair<string, object?>>? Last { get; private set; }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

    public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Last = state as IReadOnlyList<KeyValuePair<string, object?>>;
    }
}
