using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;

namespace OpenApiSample;

public static class SelfTest
{
    static int _pass, _fail;

    static void Check(string name, bool ok, string? detail = null)
    {
        if (ok) _pass++; else _fail++;
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}{(ok || detail is null ? "" : "  -> " + detail)}");
    }

    public static async Task<int> RunAsync()
    {
        // Test-only symmetric key; a real deployment uses the issuer's JWKS via Authority.
        var key = new SymmetricSecurityKey(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());
        await using var app = SampleApi.Build([], key, b => { b.WebHost.UseTestServer(); b.Logging.SetMinimumLevel(LogLevel.None); });
        await app.StartAsync();
        using var http = app.GetTestClient();

        // ---- 1. the generated OpenAPI document
        var res = await http.GetAsync("/openapi/v1.json");
        var json = await res.Content.ReadAsStringAsync();
        Check("GET /openapi/v1.json is 200 application/json", res.StatusCode == HttpStatusCode.OK && res.Content.Headers.ContentType?.MediaType == "application/json");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var version = root.GetProperty("openapi").GetString()!;
        Check("openapi version is 3.1.x", version.StartsWith("3.1."), version); Console.WriteLine($"INFO  openapi = {version}; Microsoft.OpenApi {typeof(OpenApiDocument).Assembly.GetName().Version}");

        var schemes = root.GetProperty("components").GetProperty("securitySchemes");
        Check("oauth2 scheme has authorizationCode flow with scopes",
            schemes.GetProperty("oauth2").GetProperty("type").GetString() == "oauth2" &&
            schemes.GetProperty("oauth2").GetProperty("flows").GetProperty("authorizationCode").GetProperty("scopes").TryGetProperty("items:write", out _));
        Check("bearer scheme is http/bearer/JWT",
            schemes.GetProperty("bearer").GetProperty("scheme").GetString() == "bearer" &&
            schemes.GetProperty("bearer").GetProperty("bearerFormat").GetString() == "JWT");

        var post = root.GetProperty("paths").GetProperty("/items").GetProperty("post");
        var sec = post.GetProperty("security");
        Check("POST /items requires oauth2 [items:write]",
            sec[0].GetProperty("oauth2")[0].GetString() == "items:write", sec.ToString());
        Check("POST /items lists 201/400/401/403",
            new[] { "201", "400", "401", "403" }.All(c => post.GetProperty("responses").TryGetProperty(c, out _)));
        Check("error responses are application/problem+json",
            post.GetProperty("responses").GetProperty("401").GetProperty("content").TryGetProperty("application/problem+json", out _));
        var ids = AllOperationIds(root).ToList();
        Check("3 operations, unique operationIds", ids.Count == 3 && ids.Distinct().Count() == 3, string.Join(",", ids));

        // ---- 2. Microsoft.OpenApi reads the document without diagnostics
        var (parsed, diag) = OpenApiDocument.Parse(json, "json");
        Check("Microsoft.OpenApi parse: 0 errors", diag!.Errors.Count == 0, string.Join("; ", diag.Errors.Select(e => e.Message)));
        Check("Microsoft.OpenApi parse: 0 warnings", diag.Warnings.Count == 0, string.Join("; ", diag.Warnings.Select(e => e.Message)));
        var problems = parsed!.Validate(ValidationRuleSet.GetDefaultRuleSet()) ?? [];
        Check("Microsoft.OpenApi default ruleset: 0 validation errors", !problems.Any(), string.Join("; ", problems.Select(e => e.Message)));

        // ---- 3. the API enforces what the document declares
        var none = await SendAsync(http, HttpMethod.Get, "/items", null);
        Check("no token -> 401 with Bearer challenge", none.Code == HttpStatusCode.Unauthorized && none.Challenge.Contains("Bearer"), none.Challenge);
        var read = Mint(key, "items:read");
        Check("items:read token can GET /items", (await SendAsync(http, HttpMethod.Get, "/items", read)).Code == HttpStatusCode.OK);
        Check("items:read token cannot POST -> 403", (await SendAsync(http, HttpMethod.Post, "/items", read, "{\"name\":\"a\",\"price\":1}")).Code == HttpStatusCode.Forbidden);
        var write = Mint(key, "items:read items:write");
        Check("items:write token can POST -> 201", (await SendAsync(http, HttpMethod.Post, "/items", write, "{\"name\":\"a\",\"price\":1}")).Code == HttpStatusCode.Created);
        Check("token for another audience -> 401", (await SendAsync(http, HttpMethod.Get, "/items", Mint(key, "items:read", audience: "api://other"))).Code == HttpStatusCode.Unauthorized);
        Check("token with typ JWT (not at+jwt) -> 401", (await SendAsync(http, HttpMethod.Get, "/items", Mint(key, "items:read", typ: "JWT"))).Code == HttpStatusCode.Unauthorized);
        Check("scope 'items:readonly' does not satisfy items:read -> 403", (await SendAsync(http, HttpMethod.Get, "/items", Mint(key, "items:readonly"))).Code == HttpStatusCode.Forbidden);

        JsonSchemaTests();
        Console.WriteLine($"{(_fail == 0 ? "ALL PASSED" : "FAILED")} ({_pass} passed, {_fail} failed)");
        return _fail == 0 ? 0 : 1;
    }

    static IEnumerable<string> AllOperationIds(JsonElement root) =>
        root.GetProperty("paths").EnumerateObject().SelectMany(p => p.Value.EnumerateObject())
            .Select(o => o.Value.GetProperty("operationId").GetString()!);

    static async Task<(HttpStatusCode Code, string Challenge)> SendAsync(HttpClient http, HttpMethod m, string url, string? token, string? body = null)
    {
        using var req = new HttpRequestMessage(m, url);
        if (token is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) req.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        using var r = await http.SendAsync(req);
        return (r.StatusCode, string.Join(",", r.Headers.WwwAuthenticate.Select(h => h.ToString())));
    }

    static string Mint(SecurityKey key, string scope, string audience = SampleApi.Audience, string typ = "at+jwt")
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = SampleApi.Issuer,
            Audience = audience,
            Subject = new ClaimsIdentity([new Claim("sub", "user-1"), new Claim("scope", scope)]),
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
            TokenType = typ,
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    // ---- JSON Schema 2020-12 checks (see reference/openapi-jsonschema.md)
    sealed record SearchArgs(string Query, int Limit = 10);

    static void JsonSchemaTests()
    {
        // generated schema
        var gen = SchemaCheck.Generate(typeof(SearchArgs));
        var genText = gen.ToJsonString(); Console.WriteLine("INFO  generated: " + genText);
        Check("generated schema declares 2020-12 and additionalProperties:false",
            gen["$schema"]!.GetValue<string>() == SchemaCheck.Dialect2020 && gen["additionalProperties"]?.GetValue<bool>() == false, genText);
        var compiled = SchemaCheck.Compile(genText);
        Check("generated schema accepts valid arguments", SchemaCheck.IsValid(compiled, "{\"query\":\"x\",\"limit\":5}"));
        Check("generated schema rejects wrong type", !SchemaCheck.IsValid(compiled, "{\"query\":\"x\",\"limit\":\"5\"}"));
        Check("generated schema rejects missing required", !SchemaCheck.IsValid(compiled, "{\"limit\":5}"));
        Check("generated schema rejects unknown property", !SchemaCheck.IsValid(compiled, "{\"query\":\"x\",\"evil\":1}"));

        // generated schema is a TYPE check only: add semantic keywords by hand
        var semantic = SchemaCheck.Compile("""
            {"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","required":["query"],
             "properties":{"query":{"type":"string","minLength":1,"maxLength":64,"pattern":"^[\\w ]+$"},
                           "limit":{"type":"integer","minimum":1,"maximum":50}},
             "additionalProperties":false}
            """);
        Check("semantic schema rejects limit 5000", !SchemaCheck.IsValid(semantic, "{\"query\":\"x\",\"limit\":5000}"));
        Check("semantic schema rejects query with '/'", !SchemaCheck.IsValid(semantic, "{\"query\":\"../etc\"}"));

        // additionalProperties:false breaks composition, unevaluatedProperties:false does not
        const string baseDef = """{"type":"object","properties":{"id":{"type":"string"}},"required":["id"]}""";
        var broken = SchemaCheck.Compile($$$"""
            {"$schema":"https://json-schema.org/draft/2020-12/schema","allOf":[{{{baseDef}}}],
             "properties":{"name":{"type":"string"}},"additionalProperties":false}
            """);
        var good = SchemaCheck.Compile($$$"""
            {"$schema":"https://json-schema.org/draft/2020-12/schema","allOf":[{{{baseDef}}}],
             "properties":{"name":{"type":"string"}},"unevaluatedProperties":false}
            """);
        const string inst = "{\"id\":\"1\",\"name\":\"n\"}";
        Check("additionalProperties:false rejects a property declared inside allOf", !SchemaCheck.IsValid(broken, inst));
        Check("unevaluatedProperties:false accepts it", SchemaCheck.IsValid(good, inst));
        Check("unevaluatedProperties:false still rejects a truly unknown property", !SchemaCheck.IsValid(good, "{\"id\":\"1\",\"name\":\"n\",\"x\":1}"));

        // $defs + local $ref
        var defs = SchemaCheck.Compile("""
            {"$schema":"https://json-schema.org/draft/2020-12/schema","$defs":{"id":{"type":"string","pattern":"^[a-z0-9-]{1,36}$"}},
             "type":"object","properties":{"a":{"$ref":"#/$defs/id"},"b":{"$ref":"#/$defs/id"}}}
            """);
        Check("local $defs/$ref validates", SchemaCheck.IsValid(defs, "{\"a\":\"x-1\",\"b\":\"y\"}") && !SchemaCheck.IsValid(defs, "{\"a\":\"X!\"}"));

        // dialect and remote $ref policy
        bool dialectRejected;
        try { SchemaCheck.Compile("{\"$schema\":\"http://json-schema.org/draft-07/schema#\",\"type\":\"object\"}"); dialectRejected = false; }
        catch (NotSupportedException) { dialectRejected = true; }
        Check("draft-07 $schema is rejected as unsupported dialect", dialectRejected);

        bool remoteBlocked;
        try
        {
            var remote = SchemaCheck.Compile("""{"$schema":"https://json-schema.org/draft/2020-12/schema","$ref":"https://example.com/other.json"}""");
            SchemaCheck.IsValid(remote, "{}");
            remoteBlocked = false;
        }
        catch (Exception) { remoteBlocked = true; }
        Check("remote $ref is not fetched (resolution fails, not 'valid')", remoteBlocked);

        // format is an annotation in 2020-12 unless the assertion vocabulary is enabled
        var fmt = SchemaCheck.Compile("""{"$schema":"https://json-schema.org/draft/2020-12/schema","type":"string","format":"email"}""");
        Console.WriteLine($"INFO  format:email on \"not-an-email\" is {(SchemaCheck.IsValid(fmt, "\"not-an-email\"") ? "valid (annotation only)" : "invalid (asserted)")} with default options");
    }
}
