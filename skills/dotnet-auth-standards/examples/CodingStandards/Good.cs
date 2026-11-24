// Good examples for reference/coding-standards.md. Built with warnings as errors.
using Microsoft.Extensions.Logging;

namespace Example.Orders;

// ---- Naming (section 1) -------------------------------------------------

public interface IOrderRepository
{
    Task<Order?> FindAsync(OrderId id, CancellationToken cancellationToken);
}

public readonly record struct OrderId(Guid Value);

public enum OrderStatus { Pending, Paid, Shipped }

[Flags]
public enum OrderPermissions { None = 0, Read = 1, Write = 2 }

public interface ISession { string Id { get; } }

public interface ISessionChannel<TSession> where TSession : ISession
{
    TSession Session { get; }
}

public sealed class OrderNotFoundException : Exception
{
    public OrderNotFoundException() { }
    public OrderNotFoundException(string message) : base(message) { }
    public OrderNotFoundException(string message, Exception innerException) : base(message, innerException) { }
}

// ---- Immutability, records, encapsulation (sections 2 and 4) ----------

public sealed record OrderLine(string Sku, int Quantity, decimal UnitPrice);

public sealed record Order(OrderId Id, string CustomerName, IReadOnlyList<OrderLine> Lines)
{
    public decimal Total => Lines.Sum(l => l.Quantity * l.UnitPrice);
}

// Encapsulation: state changes only through a method that enforces the invariant.
public sealed class Basket
{
    private const int MaxLines = 3;
    private static readonly string[] s_forbiddenSkus = ["BANNED"];

    private readonly List<OrderLine> _lines = [];

    public IReadOnlyList<OrderLine> Lines => _lines;

    public void Add(OrderLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(line.Quantity);
        if (s_forbiddenSkus.Contains(line.Sku))
        {
            throw new InvalidOperationException($"SKU {line.Sku} cannot be ordered.");
        }

        if (_lines.Count >= MaxLines)
        {
            throw new InvalidOperationException($"A basket holds at most {MaxLines} lines.");
        }

        _lines.Add(line);
    }
}

// ---- SOLID (section 3) ---------------------------------------------------

// S: one reason to change each. Pricing is separate from persistence and notification.
public interface IDiscountRule
{
    decimal Apply(decimal subtotal);
}

public sealed class NoDiscount : IDiscountRule
{
    public decimal Apply(decimal subtotal) => subtotal;
}

public sealed class PercentDiscount(decimal percent) : IDiscountRule
{
    public decimal Apply(decimal subtotal) => subtotal - subtotal * percent / 100m;
}

// O: new behaviour = new IDiscountRule class; PriceCalculator is not edited.
public sealed class PriceCalculator(IDiscountRule rule)
{
    public decimal Price(Order order) => rule.Apply(order.Total);
}

// I: small role interfaces; a read-only consumer cannot call writes.
public interface IOrderReader { Task<Order?> FindAsync(OrderId id, CancellationToken cancellationToken); }
public interface IOrderWriter { Task SaveAsync(Order order, CancellationToken cancellationToken); }

public sealed class InMemoryOrders : IOrderReader, IOrderWriter, IOrderRepository
{
    private readonly Dictionary<OrderId, Order> _orders = [];

    public Task<Order?> FindAsync(OrderId id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_orders.GetValueOrDefault(id));
    }

    public Task SaveAsync(Order order, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _orders[order.Id] = order;
        return Task.CompletedTask;
    }
}

// L: Shape contract = Area is width*height computed from immutable state; substitutes keep it.
public abstract class Shape
{
    public abstract double Area { get; }
}

public sealed class Rectangle(double width, double height) : Shape
{
    public override double Area => width * height;
}

public sealed class Square(double side) : Shape
{
    public override double Area => side * side;
}

// D: high-level code depends on an abstraction it owns, injected through the constructor.
public interface IClock { DateTimeOffset UtcNow { get; } }

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class InvoiceStamper(IClock clock)
{
    public string Stamp(OrderId id) => $"{id.Value:N}@{clock.UtcNow:O}";
}

// Composition over inheritance: add logging behaviour by wrapping, not by deriving.
public sealed class AuditedReader(IOrderReader inner, List<string> audit) : IOrderReader
{
    public async Task<Order?> FindAsync(OrderId id, CancellationToken cancellationToken)
    {
        var order = await inner.FindAsync(id, cancellationToken);
        audit.Add($"find {id.Value:N} found={order is not null}");
        return order;
    }
}

// ---- Async and exceptions (section 4) --------------------------------------

public static class Guards
{
    // Non-async wrapper: argument errors are thrown synchronously (best-practices-for-exceptions).
    public static Task<int> LengthAsync(string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        return LengthCoreAsync(text, cancellationToken);
    }

    private static async Task<int> LengthCoreAsync(string text, CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        return text.Length;
    }

    // async method: the same null check would surface only when the task is awaited.
    public static async Task<int> LengthLazyAsync(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        await Task.Yield();
        return text.Length;
    }

    public static void RethrowCorrectly(Action action)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException)
        {
            // log, then rethrow with the original stack trace
            throw;
        }
    }

    public static void RethrowBadly(Action action)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException ex)
        {
#pragma warning disable CA2200 // demonstration only: this is the anti-pattern
            throw ex;
#pragma warning restore CA2200
        }
    }

    public static void Thrower() => throw new InvalidOperationException("boom");
}

// ---- Logging (section 4) ---------------------------------------------------

public static partial class OrderLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Order {OrderId} paid, total {Total}")]
    public static partial void OrderPaid(ILogger logger, Guid orderId, decimal total);
}
