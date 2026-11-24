using System.ComponentModel.DataAnnotations;
using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json;

namespace ProductionSamples;

/// <summary>
/// Input DTO: ONLY the fields a client may set. No Id, no OwnerSub, no IsAdmin, no Status.
/// Mass assignment (CWE-915) happens when the bound type has more properties than the client should control.
/// </summary>
public sealed record CreateOrderRequest(
    [property: Required, StringLength(80, MinimumLength = 1)] string ProductName,
    [property: Range(1, 1000)] int Quantity,
    [property: EmailAddress, StringLength(254)] string? NotifyEmail);

/// <summary>Internal entity: the server decides every sensitive field.</summary>
public sealed record Order(Guid Id, string OwnerSub, string ProductName, int Quantity, string? NotifyEmail, string Status, bool IsAdminOrder);

/// <summary>Output DTO: never return the entity directly.</summary>
public sealed record OrderResponse(Guid Id, string ProductName, int Quantity, string Status);

public sealed class OrderStore
{
    public ConcurrentDictionary<Guid, Order> Orders { get; } = new();
}

public static class InputValidation
{
    /// <summary>Allow-list for client-chosen sort fields: never put a client string into a query or expression.</summary>
    public static readonly IReadOnlySet<string> SortFields = new HashSet<string>(StringComparer.Ordinal) { "created", "quantity" };

    public static IServiceCollection AddSampleValidation(this IServiceCollection services)
    {
        services.AddValidation();                       // .NET 10: DataAnnotations on minimal API parameters -> automatic 400 problem details
        services.ConfigureHttpJsonOptions(o =>
            o.SerializerOptions.UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow);   // unknown JSON member -> 400
        services.AddProblemDetails();
        return services;
    }

    public static IEndpointRouteBuilder MapSampleOrders(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/orders");

        g.MapPost("/", (CreateOrderRequest req, ClaimsPrincipal user, OrderStore store) =>
        {
            var sub = user.FindFirstValue("sub");
            if (sub is null) return Results.Unauthorized();

            // Explicit mapping: server-controlled fields are set here and nowhere else.
            var order = new Order(Guid.NewGuid(), sub, req.ProductName.Trim(), req.Quantity, req.NotifyEmail, "pending", IsAdminOrder: false);
            store.Orders[order.Id] = order;
            AppTelemetry.OrdersCreated.Add(1);
            return Results.Created($"/orders/{order.Id}", new OrderResponse(order.Id, order.ProductName, order.Quantity, order.Status));
        });

        // {id:guid}: a route constraint rejects malformed ids before the handler runs (404, no handler code).
        g.MapGet("/{id:guid}", (Guid id, ClaimsPrincipal user, OrderStore store) =>
            store.Orders.TryGetValue(id, out var o) && o.OwnerSub == user.FindFirstValue("sub")     // object-level authorization (BOLA)
                ? Results.Ok(new OrderResponse(o.Id, o.ProductName, o.Quantity, o.Status))
                : Results.NotFound());                                                              // 404 for foreign orders: do not confirm existence

        g.MapGet("/", (string? sort, int? take, ClaimsPrincipal user, OrderStore store) =>
        {
            if (sort is not null && !SortFields.Contains(sort))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["sort"] = [$"Allowed: {string.Join(", ", SortFields)}"] });
            var n = Math.Clamp(take ?? 20, 1, 100);                                                 // always cap page size
            var sub = user.FindFirstValue("sub");
            return Results.Ok(store.Orders.Values.Where(o => o.OwnerSub == sub).Take(n)
                .Select(o => new OrderResponse(o.Id, o.ProductName, o.Quantity, o.Status)));
        });

        return app;
    }

    /// <summary>Strict deserialization helper for non-endpoint code (queues, files): same rule, unknown members rejected.</summary>
    public static readonly JsonSerializerOptions Strict = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };
}
