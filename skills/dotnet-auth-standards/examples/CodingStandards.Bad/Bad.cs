// Bad examples for reference/coding-standards.md. Built to SHOW the warnings; never copy.
using Microsoft.Extensions.Logging;

namespace Example.Bad;

// Naming violations (the .editorconfig turns these into IDE1006 warnings).
public interface orderStore { }                      // interface without I, camelCase type

public class order_service                           // underscores in a type name
{
    private int count;                               // private field without _
    private static int counter;                      // static field without s_
    public const int max_lines = 3;                  // constant not PascalCase
    public async Task Load() { await Task.Yield(); } // async without Async suffix
}

// Nullable reference types: dereferencing a maybe-null value.
public static class Nullables
{
    public static int Length(string? text) => text.Length;       // CS8602
    public static string Name { get; set; } = null;              // CS8625
}

// S: one class with three reasons to change.
public class OrderManager
{
    public void Save(string order) { /* SQL here */ }
    public void SendEmail(string order) { /* SMTP here */ }
    public string RenderPdf(string order) => order;
}

// O: every new customer type edits this method.
public enum CustomerKind { Regular, Vip, Partner }

public static class DiscountSwitch
{
    public static decimal Apply(CustomerKind kind, decimal subtotal) => kind switch
    {
        CustomerKind.Regular => subtotal,
        CustomerKind.Vip => subtotal * 0.9m,
        _ => throw new NotSupportedException(),
    };
}

// L: Square is-a Rectangle, but breaks the Rectangle contract (width and height independent).
public class MutableRectangle
{
    public virtual double Width { get; set; }
    public virtual double Height { get; set; }
    public double Area => Width * Height;
}

public class MutableSquare : MutableRectangle
{
    public override double Width { get => base.Width; set { base.Width = value; base.Height = value; } }
    public override double Height { get => base.Height; set { base.Width = value; base.Height = value; } }
}

// I: a read-only consumer is forced to implement writes.
public interface IFatStore
{
    string Read(int id);
    void Write(int id, string value);
    void Delete(int id);
}

public class ReadOnlyStore : IFatStore
{
    public string Read(int id) => "x";
    public void Write(int id, string value) => throw new NotSupportedException();
    public void Delete(int id) => throw new NotSupportedException();
}

// D: the high-level class creates its own low-level dependency and reads the clock directly.
public class InvoiceNumber
{
    public string Next() => $"INV-{DateTime.UtcNow:yyyyMMdd}";   // untestable without changing the system clock
}

// Async and exceptions.
public static class AsyncAndErrors
{
    public static async void FireAndForget() { await Task.Yield(); throw new InvalidOperationException("lost"); }  // async void

    public static string Swallow(Func<string> f)
    {
        try { return f(); }
        catch (Exception) { return ""; }            // swallows everything, including bugs
    }

    public static void LogBadly(ILogger logger, Guid id) => logger.LogInformation($"Order {id} paid");   // CA2254
}
