using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

namespace OpenApiSample;

public sealed record Item(int Id, string Name, decimal Price);
public sealed record NewItem(string Name, decimal Price);

/// <summary>Endpoint metadata: the OAuth scopes an endpoint needs. Read by the OpenAPI operation transformer and by the policy.</summary>
public sealed record RequiredScopes(params string[] Scopes);

public static class SampleApi
{
    public const string OAuthScheme = "oauth2";
    public const string BearerScheme = "bearer";
    public const string Audience = "api://items";
    public const string Issuer = "https://auth.example.com";

    public static WebApplication Build(string[] args, SecurityKey? testKey = null, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        configure?.Invoke(builder);

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = Issuer,
                    ValidAudience = Audience,   // this API only (RFC 8707 / 9068)
                    ValidTypes = ["at+jwt"],
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256, SecurityAlgorithms.HmacSha256],
                    ClockSkew = TimeSpan.FromSeconds(30),
                    // Production: set o.Authority and drop IssuerSigningKey so keys come from the issuer's JWKS.
                    IssuerSigningKey = testKey,
                };
            });

        builder.Services.AddAuthorizationBuilder()
            .AddPolicy("items:read", p => p.RequireAuthenticatedUser().RequireAssertion(c => HasScope(c.User, "items:read")))
            .AddPolicy("items:write", p => p.RequireAuthenticatedUser().RequireAssertion(c => HasScope(c.User, "items:write")));

        builder.Services.AddOpenApi(o =>
        {
            o.AddDocumentTransformer<SecuritySchemeTransformer>();
            o.AddOperationTransformer<OperationSecurityTransformer>();
        });

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapOpenApi();                       // GET /openapi/v1.json

        var items = new List<Item> { new(1, "widget", 9.99m) };
        var group = app.MapGroup("/items").WithTags("Items");

        group.MapGet("/", () => TypedResults.Ok(items))
            .WithName("ListItems").WithSummary("List items")
            .RequireAuthorization("items:read").WithMetadata(new RequiredScopes("items:read"))
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{id:int}", (int id) => items.FirstOrDefault(i => i.Id == id) is { } i
                ? Results.Ok(i)
                : Results.Problem(statusCode: 404, title: "Item not found"))
            .WithName("GetItem").WithSummary("Get one item")
            .RequireAuthorization("items:read").WithMetadata(new RequiredScopes("items:read"))
            .Produces<Item>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/", (NewItem n) =>
            {
                var created = new Item(items.Count + 1, n.Name, n.Price);
                items.Add(created);
                return TypedResults.Created($"/items/{created.Id}", created);
            })
            .WithName("CreateItem").WithSummary("Create an item")
            .RequireAuthorization("items:write").WithMetadata(new RequiredScopes("items:write"))
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    // scope is one space-delimited string: split it, never Contains on the raw string (RFC 6749 3.3).
    static bool HasScope(ClaimsPrincipal user, string scope) =>
        user.FindFirst("scope")?.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(scope) == true;
}

/// <summary>Adds the securitySchemes to components: OAuth 2.0 authorization code (documentation of where tokens come from) and HTTP bearer JWT.</summary>
internal sealed class SecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken ct)
    {
        document.Info = new OpenApiInfo { Title = "Items API", Version = "1.0.0", Description = "Sample API; hosts are example domains." };
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>
        {
            [SampleApi.OAuthScheme] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.OAuth2,
                Description = "Authorization code with PKCE. The API validates JWT access tokens (RFC 9068) whose audience is this API.",
                Flows = new OpenApiOAuthFlows
                {
                    AuthorizationCode = new OpenApiOAuthFlow
                    {
                        AuthorizationUrl = new Uri("https://auth.example.com/authorize"),
                        TokenUrl = new Uri("https://auth.example.com/token"),
                        Scopes = new Dictionary<string, string>
                        {
                            ["items:read"] = "Read items",
                            ["items:write"] = "Create items",
                        },
                    },
                },
            },
            [SampleApi.BearerScheme] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Access token in the Authorization header (never in the URL).",
            },
        };
        return Task.CompletedTask;
    }
}

/// <summary>Adds a security requirement to every operation that has <see cref="RequiredScopes"/> metadata.</summary>
internal sealed class OperationSecurityTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken ct)
    {
        var scopes = context.Description.ActionDescriptor.EndpointMetadata.OfType<RequiredScopes>().FirstOrDefault();
        if (scopes is null) return Task.CompletedTask;

        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(SampleApi.OAuthScheme, context.Document)] = [.. scopes.Scopes],
        });
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(SampleApi.BearerScheme, context.Document)] = [],
        });
        return Task.CompletedTask;
    }
}
