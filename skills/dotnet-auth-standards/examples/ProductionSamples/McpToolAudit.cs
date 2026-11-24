using System.ComponentModel;
using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ProductionSamples;

public static class Scopes   // scope is space-delimited: split, never Contains() on the raw string
{
    public static bool Has(ClaimsPrincipal u, string scope) =>
        u.FindAll("scope").SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
         .Contains(scope, StringComparer.Ordinal);
}

[McpServerToolType]
public sealed class InvoiceTools
{
    [McpServerTool(Name = "get_invoice", ReadOnly = true, Idempotent = true, Destructive = false)]
    [Description("Read an invoice.")]
    public string Get(string id) => "invoice " + id;

    [McpServerTool(Name = "export_invoices", ReadOnly = true)]
    [Description("Export all invoices (no [Authorize] attribute: protected only by the ToolPolicy table).")]
    public string Export() => "exported";

    [Authorize(Policy = "tools-write")]
    [McpServerTool(Name = "delete_invoice", Destructive = true)]
    [Description("Delete an invoice.")]
    public string Delete(string id) => "deleted " + id;
}

/// <summary>Per-tool authorization table: tool name -> scope that must be in the token. Unknown tool = deny.</summary>
public sealed class ToolPolicy(IReadOnlyDictionary<string, string> requiredScope)
{
    public bool IsAllowed(string? tool, ClaimsPrincipal? user) =>
        tool is not null && user is not null && requiredScope.TryGetValue(tool, out var scope) && Scopes.Has(user, scope);
}

public static class McpToolAudit
{
    /// <summary>
    /// The call-tool filter from reference/mcp-ai.md section 7, extended with: per-tool authorization (deny by default),
    /// audit of denied and failed calls, exception handling, and the "McpAudit" logger category.
    /// Audit records tool NAME, subject, outcome, duration. They never contain the arguments (may hold secrets or PII).
    /// </summary>
    public static IMcpServerBuilder AddAuditedTools(this IServiceCollection services, ToolPolicy policy)
    {
        services.AddSingleton(policy);
        return services.AddMcpServer()
            .WithHttpTransport(o => o.Stateless = true)
            .WithRequestFilters(f => f.AddCallToolFilter(next => async (ctx, ct) =>
            {
                var log = ctx.Services!.GetRequiredService<ILoggerFactory>().CreateLogger("McpAudit");
                var tool = LogSanitizer.Clean(ctx.Params?.Name);
                var sub = LogSanitizer.Clean(ctx.User?.FindFirst("sub")?.Value ?? "unknown");
                var sw = Stopwatch.StartNew();

                if (!ctx.Services!.GetRequiredService<ToolPolicy>().IsAllowed(ctx.Params?.Name, ctx.User))
                {
                    log.LogWarning("tool={Tool} sub={Sub} outcome={Outcome} ms={Ms}", tool, sub, "denied", sw.ElapsedMilliseconds);
                    return new CallToolResult
                    {
                        IsError = true,
                        Content = [new TextContentBlock { Text = "Not authorized for this tool." }],   // no detail about why
                    };
                }

                try
                {
                    var result = await next(ctx, ct);
                    log.LogInformation("tool={Tool} sub={Sub} outcome={Outcome} ms={Ms}",
                        tool, sub, result.IsError == true ? "error" : "ok", sw.ElapsedMilliseconds);
                    return result;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log.LogError(ex, "tool={Tool} sub={Sub} outcome={Outcome} ms={Ms}", tool, sub, "exception", sw.ElapsedMilliseconds);
                    throw;
                }
            }))
            .AddAuthorizationFilters()          // REQUIRED: without it tools/list throws because [Authorize] metadata is present (tested)
            .WithTools<InvoiceTools>();
    }

    /// <summary>Authentication block of reference/mcp-ai.md section 4, compiled as written (names changed only where noted).</summary>
    public static void AddMcpAuthAsDocumented(WebApplicationBuilder builder)
    {
        const string resource = "https://mcp.example.com/mcp";   // canonical URI == audience
        const string issuer = "https://auth.example.com";
        const string metadataUrl = "https://mcp.example.com/.well-known/oauth-protected-resource/mcp";

        builder.Services.AddAuthentication(o =>
        {
            o.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            o.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            o.DefaultForbidScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(o =>
        {
            o.Authority = issuer;
            o.MapInboundClaims = false;            // keep "sub" and "scope" as sent
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = issuer, ValidateIssuer = true,
                ValidAudience = resource, ValidateAudience = true,
                ValidTypes = ["at+jwt"], ValidAlgorithms = ["RS256", "ES256"],
                ClockSkew = TimeSpan.FromSeconds(30),
            };
            o.Events = new JwtBearerEvents
            {
                OnChallenge = ctx =>
                {
                    ctx.HandleResponse();
                    ctx.Response.StatusCode = 401;
                    var err = ctx.AuthenticateFailure is null ? "" : "error=\"invalid_token\", ";
                    ctx.Response.Headers.WWWAuthenticate = $"Bearer {err}resource_metadata=\"{metadataUrl}\"";
                    return Task.CompletedTask;
                },
                OnForbidden = ctx =>
                {
                    ctx.Response.Headers.WWWAuthenticate =
                        $"Bearer error=\"insufficient_scope\", scope=\"tools:read\", resource_metadata=\"{metadataUrl}\"";
                    return Task.CompletedTask;
                },
            };
        })
        .AddMcp(o => o.ResourceMetadata = new ProtectedResourceMetadata
        {
            Resource = resource,
            AuthorizationServers = { issuer },
            ScopesSupported = ["tools:read"],
        });

        builder.Services.AddAuthorization(o =>
        {
            o.AddPolicy("mcp-access", p => p.RequireAuthenticatedUser()
                .RequireAssertion(c => Scopes.Has(c.User, "tools:read")));
            o.AddPolicy("tools-write", p => p.RequireAssertion(c => Scopes.Has(c.User, "tools:write")));
        });

        builder.Services.AddMcpServer()
            .WithHttpTransport(o => o.Stateless = true)
            .AddAuthorizationFilters()          // REQUIRED for [Authorize] on tools
            .WithTools<InvoiceTools>();
    }
}
