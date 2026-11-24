using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace ProductionSamples;

/// <summary>
/// Executable checks. Run: dotnet run -- selftest. Everything runs in memory (TestServer, in-memory streams):
/// no TCP port is opened.
/// </summary>
public static partial class SelfTest
{
    private static int _fail;
    private static int _count;

    private static void Check(string name, bool ok, string? detail = null)
    {
        _count++;
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name}{(!ok && detail is not null ? "  <- " + detail : "")}");
        if (!ok) _fail++;
    }

    public static async Task<int> RunAsync()
    {
        await Run("rate limiting", RateLimitChecks);
        await Run("audit log", AuditChecks);
        await Run("safe logging", SafeLoggingChecks);
        await Run("observability", ObservabilityChecks);
        await Run("resilience", ResilienceChecks);
        await Run("security headers", SecurityHeaderChecks);
        await Run("input validation", InputValidationChecks);
        await Run("webhook", WebhookChecks);
        await Run("mcp", McpChecks);
        await Run("tls", TlsChecks);
        Console.WriteLine(_fail == 0 ? $"ALL PASSED ({_count} checks)" : $"{_fail} FAILED of {_count}");
        return _fail == 0 ? 0 : 1;
    }

    private static string Where(Exception ex) => string.Join(" | ",
        (ex.StackTrace ?? "").Split((char)10).Where(l => l.Contains("SelfTest")).Take(2).Select(l => l.Trim()));

    private static async Task Run(string area, Func<Task> body)
    {
        Console.WriteLine($"--- {area}");
        try { await body(); }
        catch (Exception ex) { Check($"{area}: unexpected exception", false, ex.GetType().Name + ": " + ex.Message + " @ " + Where(ex)); }
    }

    // ---------- infrastructure ----------
    private sealed record Entry(string Category, LogLevel Level, int EventId, string Message,
        IReadOnlyList<KeyValuePair<string, object?>> State, List<object?> Scopes);

    private sealed class CapturingProvider : ILoggerProvider, ISupportExternalScope
    {
        private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();
        public List<Entry> Entries { get; } = [];
        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;
        public ILogger CreateLogger(string categoryName) => new Capture(this, categoryName);
        public void Dispose() { }

        private sealed class Capture(CapturingProvider p, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => p._scopes.Push(state);
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex, Func<TState, Exception?, string> fmt)
            {
                var scopes = new List<object?>();
                p._scopes.ForEachScope((s, l) => l.Add(s), scopes);
                var kv = (state as IEnumerable<KeyValuePair<string, object?>>)?.ToList() ?? [];
                lock (p.Entries) p.Entries.Add(new Entry(category, level, id.Id, fmt(state, ex) + (ex is null ? "" : " EX: " + ex.GetType().Name + ": " + ex.Message), kv, scopes));
            }
        }
    }

    /// <summary>Test stand-in for authentication: user "sub" and "scope" come from request headers. NOT for production.</summary>
    private static IApplicationBuilder UseFakeUser(this IApplicationBuilder app) => app.Use((ctx, next) =>
    {
        var claims = new List<Claim>();
        if (ctx.Request.Headers.TryGetValue("X-Sub", out var sub)) claims.Add(new Claim("sub", sub.ToString()));
        if (ctx.Request.Headers.TryGetValue("X-Scope", out var scope)) claims.Add(new Claim("scope", scope.ToString()));
        if (claims.Count > 0) ctx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        return next();
    });

    private static async Task<(WebApplication App, HttpClient Client)> StartAsync(
        Action<WebApplicationBuilder> configure, Action<WebApplication> pipeline, string? baseAddress = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        configure(builder);
        var app = builder.Build();
        pipeline(app);
        await app.StartAsync();
        var client = app.GetTestClient();
        if (baseAddress is not null) client.BaseAddress = new Uri(baseAddress);
        return (app, client);
    }

    private static HttpRequestMessage Req(HttpMethod m, string url, string? sub = null)
    {
        var r = new HttpRequestMessage(m, url);
        if (sub is not null) r.Headers.Add("X-Sub", sub);
        return r;
    }

    // ---------- 1. rate limiting ----------
    private static async Task RateLimitChecks()
    {
        var gate = new TaskCompletionSource();       // releases the slow request
        var entered = new TaskCompletionSource();    // signals the slow request is inside the handler
        var (app, http) = await StartAsync(
            b => b.Services.AddSampleRateLimiting(new RateLimiting.Settings(2, TimeSpan.FromHours(1), 1)),
            a =>
            {
                a.UseFakeUser();
                a.UseRateLimiter();
                a.MapGet("/fixed", () => "ok").RequireRateLimiting(RateLimiting.Fixed);
                a.MapGet("/sliding", () => "ok").RequireRateLimiting(RateLimiting.Sliding);
                a.MapGet("/bucket", () => "ok").RequireRateLimiting(RateLimiting.Bucket);
                a.MapGet("/client", () => "ok").RequireRateLimiting(RateLimiting.PerClient);
                a.MapGet("/slow", async () => { entered.TrySetResult(); await gate.Task; return "done"; })
                    .RequireRateLimiting(RateLimiting.Concurrency);
            });
        await using (app)
        {
            foreach (var path in new[] { "/fixed", "/sliding", "/bucket" })
            {
                var a = await http.GetAsync(path);
                var b = await http.GetAsync(path);
                var c = await http.GetAsync(path);
                Check($"{path}: first two requests 200", a.StatusCode == HttpStatusCode.OK && b.StatusCode == HttpStatusCode.OK);
                Check($"{path}: third request is 429", c.StatusCode == HttpStatusCode.TooManyRequests, c.StatusCode.ToString());
                var ra = c.Headers.TryGetValues("Retry-After", out var v) ? v.First() : null;
                Check($"{path}: 429 carries numeric Retry-After >= 1 (got '{ra}')", int.TryParse(ra, out var sec) && sec >= 1);
            }

            var body = await (await http.GetAsync("/fixed")).Content.ReadAsStringAsync();
            Check("429 body is a small JSON problem without internals", body.Contains("Too many requests") && !body.Contains("Exception"));

            // per-client partitioning: A is limited, B is not
            var statusA = new List<HttpStatusCode>();
            for (var i = 0; i < 3; i++) statusA.Add((await http.SendAsync(Req(HttpMethod.Get, "/client", "alice"))).StatusCode);
            var statusB = (await http.SendAsync(Req(HttpMethod.Get, "/client", "bob"))).StatusCode;
            Check("per-client: alice gets 200,200,429", statusA[0] == HttpStatusCode.OK && statusA[1] == HttpStatusCode.OK && statusA[2] == HttpStatusCode.TooManyRequests,
                string.Join(",", statusA));
            Check("per-client: bob is unaffected by alice's limit (200)", statusB == HttpStatusCode.OK);

            // concurrency: one request in flight, the second is rejected, then slots free up
            var first = http.GetAsync("/slow");
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var second = await http.GetAsync("/slow");
            Check("concurrency: second simultaneous request is 429", second.StatusCode == HttpStatusCode.TooManyRequests);
            Check("concurrency: 429 has Retry-After fallback", second.Headers.Contains("Retry-After"));
            gate.SetResult();
            Check("concurrency: first request completes 200", (await first).StatusCode == HttpStatusCode.OK);
            Check("concurrency: slot is free again (200)", (await http.GetAsync("/slow")).StatusCode == HttpStatusCode.OK);
        }
    }

    // ---------- 2. audit log ----------
    private static Task AuditChecks()
    {
        var cap = new CapturingProvider();
        using var lf = LoggerFactory.Create(b => b.AddProvider(cap));
        var audit = new AuditLogger(lf);
        audit.Write(new AuditEvent("invoice.delete", "bob\r\n2026-01-01 FAKE audit line", "invoice/42", "success",
            Email: "alice@example.com", ClientIp: "203.0.113.77", CorrelationId: "abc12345"));

        var e = cap.Entries.Single();
        Check("audit: own logger category 'Audit'", e.Category == "Audit", e.Category);
        Check("audit: email masked (a***@example.com)", e.Message.Contains("a***@example.com"));
        Check("audit: raw email absent", !e.Message.Contains("alice@"));
        Check("audit: ip truncated to /24", e.Message.Contains("ip=203.0.113.0") && !e.Message.Contains("203.0.113.77"));
        Check("audit: log forging attempt stays on ONE line", !e.Message.Contains('\n') && !e.Message.Contains('\r'));
        Console.WriteLine("  state keys: " + string.Join(",", e.State.Select(p => p.Key)));
        Check("audit: structured state has Action, Actor, Outcome", new[] { "Action", "Actor", "Outcome" }.All(k => e.State.Any(p => p.Key == k)));
        Check("audit: event id 9000, level Information", e.EventId == 9000 && e.Level == LogLevel.Information);
        Check("Pii.MaskKeepLast4", Pii.MaskKeepLast4("4111111111111111") == "************1111");
        Check("Pii.MaskEmail on garbage does not throw", Pii.MaskEmail("no-at-sign") == "***" && Pii.MaskEmail(null) == "");
        Check("Pii.TruncateIp ipv6", Pii.TruncateIp("2001:db8:85a3:8d3:1319:8a2e:370:7348") == "2001:db8:85a3::");
        return Task.CompletedTask;
    }

    // ---------- 3. safe logging ----------
    private static async Task SafeLoggingChecks()
    {
        Check("Clean replaces CR/LF with _", LogSanitizer.Clean("a\r\nb") == "a__b");
        Check("Clean replaces unicode line separator", LogSanitizer.Clean("a" + (char)0x2028 + "b") == "a_b");
        Check("Clean caps length at 256", LogSanitizer.Clean(new string('x', 10_000)).Length == 256);
        Check("Clean(null) is empty", LogSanitizer.Clean(null) == "");
        Check("Redact bearer token", Redactor.Redact("Authorization: Bearer abc.def-123") == "Authorization: Bearer [REDACTED]");
        const string jwt = "eyJhbGciOiJSUzI1NiJ9.eyJzdWIiOiIxIn0.c2ln";
        Check("Redact bare JWT", Redactor.Redact($"token was {jwt} ok") == "token was [REDACTED] ok");
        var q = Redactor.Redact("code=1&client_secret=s3cr3t&access_token=zzz&page=2");
        Check("Redact key=value secrets, keep other params", !q.Contains("s3cr3t") && !q.Contains("zzz") && q.Contains("page=2") && q.Contains("client_secret=[REDACTED]"), q);
        Check("Redact password: form", !Redactor.Redact("password: hunter2").Contains("hunter2"));

        var cap = new CapturingProvider();
        using (var lf = LoggerFactory.Create(b => b.AddProvider(cap)))
        {
            var log = lf.CreateLogger("App");
            log.RequestReceived("GET", LogSanitizer.Clean("/x\r\nFAKE"), Redactor.Redact("access_token=abc"));
            log.LoginFailed(LogSanitizer.Clean("admin\nINFO user admin logged in"));
        }
        Check("LoggerMessage: generated method writes event 1000 with template values",
            cap.Entries[0].EventId == 1000 && cap.Entries[0].Message == "request GET /x__FAKE query=access_token=[REDACTED]", cap.Entries[0].Message);
        Check("LoggerMessage: injected newline cannot add a log line", cap.Entries[1].Message == "login failed for admin_INFO user admin logged in");
        Check("LoggerMessage: structured property {User} kept", cap.Entries[1].State.Any(p => p.Key == "User"));

        // correlation id middleware
        var capHost = new CapturingProvider();
        var (app, http) = await StartAsync(
            b => b.Logging.AddProvider(capHost),
            a =>
            {
                a.UseMiddleware<CorrelationIdMiddleware>();
                a.MapGet("/c", (ILoggerFactory f) => { f.CreateLogger("Handler").LogInformation("inside"); return "ok"; });
            });
        await using (app)
        {
            var good = new HttpRequestMessage(HttpMethod.Get, "/c");
            good.Headers.Add("X-Correlation-Id", "req-12345678");
            var r1 = await http.SendAsync(good);
            Check("correlation: valid incoming id is echoed", r1.Headers.GetValues("X-Correlation-Id").Single() == "req-12345678");

            var bad = new HttpRequestMessage(HttpMethod.Get, "/c");
            bad.Headers.Add("X-Correlation-Id", "x y<script>alert(1)</script>");
            var r2 = await http.SendAsync(bad);
            var id2 = r2.Headers.GetValues("X-Correlation-Id").Single();
            Check("correlation: unsafe incoming id replaced by a generated 32-hex id", id2.Length == 32 && id2.All(Uri.IsHexDigit), id2);

            var r3 = await http.GetAsync("/c");
            Check("correlation: absent id is generated", r3.Headers.Contains("X-Correlation-Id"));

            var inside = capHost.Entries.First(en => en.Category == "Handler");
            var scoped = inside.Scopes.OfType<IEnumerable<KeyValuePair<string, object>>>().SelectMany(s => s)
                .Any(kv => kv.Key == "CorrelationId" && (string)kv.Value == "req-12345678");
            Check("correlation: id is in the log scope of handler log lines", scoped);
        }
    }

    // ---------- 4. observability ----------
    private static async Task ObservabilityChecks()
    {
        var activities = new List<Activity>();
        var metrics = new List<Metric>();
        var (app, http) = await StartAsync(
            b =>
            {
                b.Services.AddSampleObservability("production-samples", useOtlp: false,
                    tracing: t => t.AddInMemoryExporter(activities),
                    metrics: m => m.AddInMemoryExporter(metrics));
                b.Services.AddSampleHealthChecks();
            },
            a =>
            {
                a.MapGet("/work", () =>
                {
                    using var act = AppTelemetry.Source.StartActivity("create-order");
                    act?.SetTag("order.items", 3);
                    AppTelemetry.OrdersCreated.Add(1);
                    return "ok";
                });
                a.MapSampleHealth();
            });
        await using (app)
        {
            await http.GetAsync("/work");
            for (var i = 0; i < 40 && !activities.Any(a => a.Kind == ActivityKind.Server); i++)
            {
                await Task.Delay(50);                                            // the server span stops just after the response is sent
                app.Services.GetRequiredService<TracerProvider>().ForceFlush();
            }
            app.Services.GetRequiredService<TracerProvider>().ForceFlush();
            app.Services.GetRequiredService<MeterProvider>().ForceFlush();

            Check("traces: ASP.NET Core request span exported", activities.Any(a => a.Kind == ActivityKind.Server), string.Join(",", activities.Select(a => a.DisplayName)));
            Check("traces: custom ActivitySource span exported", activities.Any(a => a.OperationName == "create-order"));
            var child = activities.FirstOrDefault(a => a.OperationName == "create-order");
            Check("traces: custom span is a child of the request span", child is not null && activities.Any(a => a.Kind == ActivityKind.Server && a.SpanId == child.ParentSpanId));
            Check("metrics: custom counter orders.created exported", metrics.Any(m => m.Name == "orders.created"));
            Check("metrics: http.server.request.duration exported", metrics.Any(m => m.Name == "http.server.request.duration"));
            Check("metrics: runtime metrics exported", metrics.Any(m => m.Name.StartsWith("process.runtime") || m.Name.StartsWith("dotnet.")), string.Join(",", metrics.Select(m => m.Name).Take(8)));

            var live = await http.GetAsync("/health/live");
            var ready = await http.GetAsync("/health/ready");
            Check("health: liveness 200 'Healthy'", live.StatusCode == HttpStatusCode.OK && await live.Content.ReadAsStringAsync() == "Healthy");
            Check("health: readiness 200 when dependency up", ready.StatusCode == HttpStatusCode.OK);

            app.Services.GetRequiredService<DependencyState>().DatabaseUp = false;
            var live2 = await http.GetAsync("/health/live");
            var ready2 = await http.GetAsync("/health/ready");
            var readyBody = await ready2.Content.ReadAsStringAsync();
            Check("health: readiness 503 when dependency down", ready2.StatusCode == HttpStatusCode.ServiceUnavailable);
            Check("health: liveness stays 200 when dependency down (no restart loop)", live2.StatusCode == HttpStatusCode.OK);
            Check("health: body is only the status word, no detail leaked", readyBody == "Unhealthy", readyBody);
        }
    }

    // ---------- 5. resilience ----------
    private sealed class ScriptedHandler(Func<int, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> script) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            script(Interlocked.Increment(ref Calls), request, ct);
    }

    private static HttpClient BuildClient(ScriptedHandler handler, Resilience.Settings s, out ServiceProvider sp)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDownstreamClient(s);
        services.AddHttpClient(Resilience.ClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IHttpClientFactory>().CreateClient(Resilience.ClientName);
    }

    private static Task<HttpResponseMessage> Respond(HttpStatusCode code) => Task.FromResult(new HttpResponseMessage(code));

    private static async Task ResilienceChecks()
    {
        var fast = new Resilience.Settings(
            TotalTimeout: TimeSpan.FromSeconds(20), AttemptTimeout: TimeSpan.FromMilliseconds(300), MaxRetries: 3, RetryDelay: TimeSpan.FromMilliseconds(5),
            BreakerFailureRatio: 0.5, BreakerMinThroughput: 4, BreakerSampling: TimeSpan.FromSeconds(30), BreakerBreakDuration: TimeSpan.FromSeconds(30));

        // retry: two 503 then 200
        var h1 = new ScriptedHandler((n, _, _) => Respond(n < 3 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK));
        using (var c = BuildClient(h1, fast with { BreakerMinThroughput = 100 }, out var sp1))
        {
            var r = await c.GetAsync("/x");
            Check($"retry: GET succeeds after two 503 (status {(int)r.StatusCode}, downstream calls {h1.Calls})", r.StatusCode == HttpStatusCode.OK && h1.Calls == 3);
            await sp1.DisposeAsync();
        }

        // no retry on 4xx
        var h2 = new ScriptedHandler((_, _, _) => Respond(HttpStatusCode.BadRequest));
        using (var c = BuildClient(h2, fast with { BreakerMinThroughput = 100 }, out var sp2))
        {
            var r = await c.GetAsync("/x");
            Check($"retry: 400 is not retried (calls {h2.Calls})", r.StatusCode == HttpStatusCode.BadRequest && h2.Calls == 1);
            await sp2.DisposeAsync();
        }

        // no retry for POST
        var h3 = new ScriptedHandler((_, _, _) => Respond(HttpStatusCode.ServiceUnavailable));
        using (var c = BuildClient(h3, fast with { BreakerMinThroughput = 100 }, out var sp3))
        {
            var r = await c.PostAsync("/x", new StringContent("{}"));
            Check($"retry: POST is not retried (calls {h3.Calls})", r.StatusCode == HttpStatusCode.ServiceUnavailable && h3.Calls == 1);
            await sp3.DisposeAsync();
        }

        // retries exhausted
        var h4 = new ScriptedHandler((_, _, _) => Respond(HttpStatusCode.InternalServerError));
        using (var c = BuildClient(h4, fast with { BreakerMinThroughput = 100 }, out var sp4))
        {
            var r = await c.GetAsync("/x");
            Check($"retry: gives up after MaxRetries (1 + 3 = 4 calls, got {h4.Calls})", r.StatusCode == HttpStatusCode.InternalServerError && h4.Calls == 4);
            await sp4.DisposeAsync();
        }

        // circuit breaker: always failing downstream; breaker opens, then calls fail fast without reaching downstream
        var h5 = new ScriptedHandler((_, _, _) => Respond(HttpStatusCode.InternalServerError));
        using (var c = BuildClient(h5, fast with { MaxRetries = 1 }, out var sp5))
        {
            Exception? broken = null;
            var sent = 0;
            for (; sent < 20 && broken is null; sent++)
            {
                try { await c.GetAsync("/x"); } catch (Exception ex) { broken = ex; }
            }
            var callsWhenOpen = h5.Calls;
            try { await c.GetAsync("/x"); } catch { /* expected */ }
            Check($"breaker: opens after failures (exception {broken?.GetType().Name}, after {sent} requests)", broken is Polly.CircuitBreaker.BrokenCircuitException);
            Check("breaker: open circuit fails fast, downstream not called again", h5.Calls == callsWhenOpen, $"{callsWhenOpen} -> {h5.Calls}");
            await sp5.DisposeAsync();
        }

        // attempt timeout
        var h6 = new ScriptedHandler(async (_, _, ct) => { await Task.Delay(Timeout.Infinite, ct); return new HttpResponseMessage(); });
        using (var c = BuildClient(h6, fast with { MaxRetries = 1, AttemptTimeout = TimeSpan.FromMilliseconds(150), BreakerMinThroughput = 100 }, out var sp6))
        {
            var sw = Stopwatch.StartNew();
            Exception? ex = null;
            try { await c.GetAsync("/x"); } catch (Exception e) { ex = e; }
            Check($"timeout: hung downstream cut off by the attempt timeout ({ex?.GetType().Name}, {sw.ElapsedMilliseconds} ms)",
                ex is Polly.Timeout.TimeoutRejectedException && sw.ElapsedMilliseconds < 3000);
            await sp6.DisposeAsync();
        }

        // Retry-After on 429 is honoured by the retry strategy (delay >= header)
        var h7 = new ScriptedHandler((n, _, _) =>
        {
            var r = new HttpResponseMessage(n == 1 ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK);
            if (n == 1) r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
            return Task.FromResult(r);
        });
        using (var c = BuildClient(h7, fast with { BreakerMinThroughput = 100 }, out var sp7))
        {
            var sw = Stopwatch.StartNew();
            var r = await c.GetAsync("/x");
            Check($"retry: Retry-After: 1 on 429 is honoured (waited {sw.ElapsedMilliseconds} ms)", r.StatusCode == HttpStatusCode.OK && sw.ElapsedMilliseconds >= 900);
            await sp7.DisposeAsync();
        }
    }

    // ---------- 6. security headers ----------
    private static async Task SecurityHeaderChecks()
    {
        var (app, http) = await StartAsync(
            b => b.Services.AddSampleSecurityHeaders(),
            a =>
            {
                a.UseHsts();
                a.UseSampleSecurityHeaders();
                a.MapGet("/ping", () => "pong");
                a.MapGet("/boom", (Func<string>)(() => throw new InvalidOperationException("secret detail")));
            },
            baseAddress: "https://api.example.test");
        await using (app)
        {
            var r = await http.GetAsync("/ping");
            string H(string n) => r.Headers.TryGetValues(n, out var v) ? string.Join(";", v) : "";
            Check("headers: X-Content-Type-Options nosniff", H("X-Content-Type-Options") == "nosniff");
            Check("headers: CSP default-src 'none' and frame-ancestors 'none'", H("Content-Security-Policy").Contains("default-src 'none'") && H("Content-Security-Policy").Contains("frame-ancestors 'none'"));
            Check("headers: X-Frame-Options DENY", H("X-Frame-Options") == "DENY");
            Check("headers: Referrer-Policy no-referrer", H("Referrer-Policy") == "no-referrer");
            Check("headers: Permissions-Policy present", H("Permissions-Policy").Contains("camera=()"));
            Check("headers: Cache-Control no-store", r.Headers.CacheControl?.NoStore == true);
            Check("headers: no Server / X-Powered-By", !r.Headers.Contains("Server") && !r.Headers.Contains("X-Powered-By"));
            Check("headers: HSTS over https (max-age 1 year, includeSubDomains)", H("Strict-Transport-Security") == "max-age=31536000; includeSubDomains", H("Strict-Transport-Security"));
            var nf = await http.GetAsync("/missing");
            Check("headers: also present on a 404", nf.StatusCode == HttpStatusCode.NotFound && nf.Headers.Contains("X-Content-Type-Options"));
            HttpResponseMessage? err = null;
            try { err = await http.GetAsync("/boom"); } catch { /* TestServer rethrows the unhandled exception */ }
            Check("headers: unhandled exception does not leak its message into a response body", err is null || !(await err.Content.ReadAsStringAsync()).Contains("secret detail"));
        }

        var (app2, http2) = await StartAsync(
            b => b.Services.AddSampleSecurityHeaders(),
            a => { a.UseHsts(); a.UseSampleSecurityHeaders(); a.MapGet("/ping", () => "pong"); },
            baseAddress: "http://api.example.test");
        await using (app2)
        {
            var r = await http2.GetAsync("/ping");
            Check("headers: no HSTS header over plain http (RFC 6797 section 7.2)", !r.Headers.Contains("Strict-Transport-Security"));
        }
    }

    // ---------- 7. input validation ----------
    private static async Task InputValidationChecks()
    {
        OrderStore? store = null;
        var (app, http) = await StartAsync(
            b => { b.Services.AddSampleValidation(); b.Services.AddSingleton<OrderStore>(); },
            a => { store = a.Services.GetRequiredService<OrderStore>(); a.UseFakeUser(); a.MapSampleOrders(); });
        await using (app)
        {
            static HttpRequestMessage Post(string json, string sub = "alice")
            {
                var r = new HttpRequestMessage(HttpMethod.Post, "/orders") { Content = new StringContent(json, Encoding.UTF8, "application/json") };
                r.Headers.Add("X-Sub", sub);
                return r;
            }

            var ok = await http.SendAsync(Post("""{"productName":"Widget","quantity":2,"notifyEmail":"a@example.com"}"""));
            var created = store!.Orders.Values.SingleOrDefault();
            Check("valid order: 201", ok.StatusCode == HttpStatusCode.Created, ok.StatusCode.ToString());
            Check("valid order: owner comes from the token, status and admin flag from the server",
                created is { OwnerSub: "alice", Status: "pending", IsAdminOrder: false });

            var before = store.Orders.Count;
            var mass = await http.SendAsync(Post("""{"productName":"Widget","quantity":1,"isAdminOrder":true,"ownerSub":"mallory","status":"shipped","id":"00000000-0000-0000-0000-000000000001"}"""));
            Check("mass assignment: extra server-owned members rejected with 400", mass.StatusCode == HttpStatusCode.BadRequest, mass.StatusCode.ToString());
            Check("mass assignment: nothing stored", store.Orders.Count == before);

            var lenient = System.Text.Json.JsonSerializer.Deserialize<CreateOrderRequest>(
                """{"productName":"W","quantity":1,"isAdminOrder":true}""", new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
            Check("mass assignment: even with lenient JSON the DTO has no property to receive isAdminOrder",
                lenient is not null && typeof(CreateOrderRequest).GetProperty("IsAdminOrder") is null);
            Check("strict options reject unknown members", Throws(() => System.Text.Json.JsonSerializer.Deserialize<CreateOrderRequest>(
                """{"productName":"W","quantity":1,"isAdminOrder":true}""", InputValidation.Strict)));

            foreach (var (name, json, member) in new[]
            {
                ("quantity 0", """{"productName":"W","quantity":0}""", "quantity"),
                ("quantity 1001", """{"productName":"W","quantity":1001}""", "quantity"),
                ("missing productName", """{"quantity":1}""", "productName"),
                ("empty productName", """{"productName":"","quantity":1}""", "productName"),
                ("productName 81 chars", $$"""{"productName":"{{new string('x', 81)}}","quantity":1}""", "productName"),
                ("bad email", """{"productName":"W","quantity":1,"notifyEmail":"not-an-email"}""", "notifyEmail"),
            })
            {
                var r = await http.SendAsync(Post(json));
                var text = await r.Content.ReadAsStringAsync();
                Check($"validation: {name} -> 400 problem details naming '{member}'",
                    r.StatusCode == HttpStatusCode.BadRequest && text.Contains(member, StringComparison.OrdinalIgnoreCase), $"{(int)r.StatusCode} {text}");
            }
            Check("validation: invalid requests stored nothing", store.Orders.Count == before);

            var id = created!.Id;
            Check("BOLA: owner reads own order 200", (await http.SendAsync(Req(HttpMethod.Get, $"/orders/{id}", "alice"))).StatusCode == HttpStatusCode.OK);
            Check("BOLA: another user gets 404, not 403 and not the order", (await http.SendAsync(Req(HttpMethod.Get, $"/orders/{id}", "bob"))).StatusCode == HttpStatusCode.NotFound);
            Check("route constraint: non-guid id is 404 before the handler", (await http.SendAsync(Req(HttpMethod.Get, "/orders/1' OR '1'='1", "alice"))).StatusCode == HttpStatusCode.NotFound);

            Check("sort allow-list: unknown field -> 400", (await http.SendAsync(Req(HttpMethod.Get, "/orders?sort=password;drop", "alice"))).StatusCode == HttpStatusCode.BadRequest);
            Check("sort allow-list: known field -> 200", (await http.SendAsync(Req(HttpMethod.Get, "/orders?sort=created", "alice"))).StatusCode == HttpStatusCode.OK);
            for (var i = 0; i < 3; i++) await http.SendAsync(Post("""{"productName":"More","quantity":1}"""));
            var page = await (await http.SendAsync(Req(HttpMethod.Get, "/orders?take=1", "alice"))).Content.ReadFromJsonAsync<List<OrderResponse>>();
            var huge = await (await http.SendAsync(Req(HttpMethod.Get, "/orders?take=2147483647", "alice"))).Content.ReadFromJsonAsync<List<OrderResponse>>();
            var neg = await (await http.SendAsync(Req(HttpMethod.Get, "/orders?take=-5", "alice"))).Content.ReadFromJsonAsync<List<OrderResponse>>();
            Check("paging: take=1 returns 1, take=-5 is clamped to 1, huge take is capped at 100", page!.Count == 1 && neg!.Count == 1 && huge!.Count == 4);
            var other = await (await http.SendAsync(Req(HttpMethod.Get, "/orders", "bob"))).Content.ReadFromJsonAsync<List<OrderResponse>>();
            Check("list: bob sees none of alice's orders", other!.Count == 0);
        }
    }

    private static bool Throws(Action a)
    {
        try { a(); return false; } catch { return true; }
    }
}
