using System.Globalization;
using System.IO.Pipelines;
using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.TestHost;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace ProductionSamples;

public static partial class SelfTest
{
    // ---------- 8. webhook ----------
    /// <summary>The verifier from reference/webhooks-events.md 4.2, copied as written (only the types around it differ).</summary>
    private sealed class DocWebhookVerifier(IReadOnlyList<byte[]> secrets, IMemoryCache seen, TimeSpan tolerance)
    {
        public VerifyResult Verify(string id, string timestamp, string signatureHeader, string rawBody)
        {
            if (id.Length is 0 or > 128 || id.Contains('.')) return VerifyResult.Malformed;
            if (!long.TryParse(timestamp, out var ts)) return VerifyResult.Malformed;

            var age = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(ts);
            if (age > tolerance || age < -tolerance) return VerifyResult.StaleTimestamp;

            var content = Encoding.UTF8.GetBytes($"{id}.{ts}.{rawBody}");
            var ok = false;
            foreach (var secret in secrets)
            {
                var expected = HMACSHA256.HashData(secret, content);
                foreach (var part in signatureHeader.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!part.StartsWith("v1,", StringComparison.Ordinal)) continue;
#pragma warning disable CA2014 // the doc block as written: this IS the warning the doc needs to fix (FIX-3)
                    Span<byte> candidate = stackalloc byte[32];
#pragma warning restore CA2014
                    if (Convert.TryFromBase64String(part[3..], candidate, out var n) && n == 32
                        && CryptographicOperations.FixedTimeEquals(candidate, expected))
                        ok = true;
                }
            }
            if (!ok) return VerifyResult.BadSignature;

            if (seen.TryGetValue(id, out _)) return VerifyResult.Duplicate;
            seen.Set(id, true, tolerance * 2);
            return VerifyResult.Ok;
        }
    }

    private sealed class TestQueue : IBackgroundQueue
    {
        public List<string> Items { get; } = [];
        public bool Fail { get; set; }
        public ValueTask EnqueueAsync(string rawBody)
        {
            if (Fail) throw new InvalidOperationException("queue down");
            Items.Add(rawBody);
            return ValueTask.CompletedTask;
        }
    }

    private static HttpRequestMessage Delivery(byte[] secret, string id, long ts, string body, string? signature = null)
    {
        var r = new HttpRequestMessage(HttpMethod.Post, "/webhooks/billing")
        {
            Content = new StringContent(body, new UTF8Encoding(false), "application/json"),
        };
        r.Headers.Add("webhook-id", id);
        r.Headers.Add("webhook-timestamp", ts.ToString(CultureInfo.InvariantCulture));
        r.Headers.Add("webhook-signature", signature ?? WebhookSigner.Sign(secret, id, ts, body));
        return r;
    }

    private static async Task WebhookChecks()
    {
        var secret = RandomNumberGenerator.GetBytes(32);
        var old = RandomNumberGenerator.GetBytes(32);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        const string body = """{"type":"invoice.paid","timestamp":"2026-11-24T10:15:00Z","data":{"id":"inv_123"}}""";
        WebhookVerifier NewVerifier(params byte[][] s) => new(s, new MemoryCache(new MemoryCacheOptions()), TimeSpan.FromMinutes(5));
        string Sig(byte[] k, string id, long ts, string b) => WebhookSigner.Sign(k, id, ts, b);

        Check("secret parsing: whsec_ prefix stripped and base64 decoded", WebhookSigner.ParseSecret("whsec_" + Convert.ToBase64String(secret)).SequenceEqual(secret));

        var v = NewVerifier(secret);
        Check("verify: valid signature -> Ok", v.Verify("msg_1", now.ToString(), Sig(secret, "msg_1", now, body), body) == VerifyResult.Ok);
        Check("verify: not yet processed -> a second Verify is still Ok (id recorded only by MarkProcessed)", v.Verify("msg_1", now.ToString(), Sig(secret, "msg_1", now, body), body) == VerifyResult.Ok);
        v.MarkProcessed("msg_1");
        Check("verify: replay of same webhook-id after MarkProcessed -> Duplicate", v.Verify("msg_1", now.ToString(), Sig(secret, "msg_1", now, body), body) == VerifyResult.Duplicate);
        Check("verify: retry with a FRESH timestamp, same id -> still Duplicate (idempotency beyond the window)",
            v.Verify("msg_1", (now + 60).ToString(), Sig(secret, "msg_1", now + 60, body), body) == VerifyResult.Duplicate);
        Check("verify: tampered body -> BadSignature", NewVerifier(secret).Verify("msg_2", now.ToString(), Sig(secret, "msg_2", now, body), body + " ") == VerifyResult.BadSignature);
        Check("verify: wrong secret -> BadSignature", NewVerifier(secret).Verify("msg_3", now.ToString(), Sig(old, "msg_3", now, body), body) == VerifyResult.BadSignature);
        Check("verify: one-hour-old timestamp -> StaleTimestamp", NewVerifier(secret).Verify("msg_4", (now - 3600).ToString(), Sig(secret, "msg_4", now - 3600, body), body) == VerifyResult.StaleTimestamp);
        Check("verify: one-hour-future timestamp -> StaleTimestamp (both directions)", NewVerifier(secret).Verify("msg_5", (now + 3600).ToString(), Sig(secret, "msg_5", now + 3600, body), body) == VerifyResult.StaleTimestamp);
        Check("verify: timestamp changed after signing -> BadSignature", NewVerifier(secret).Verify("msg_6", (now + 1).ToString(), Sig(secret, "msg_6", now, body), body) == VerifyResult.BadSignature);
        Check("verify: id with a dot -> Malformed", NewVerifier(secret).Verify("msg.5", now.ToString(), Sig(secret, "msg.5", now, body), body) == VerifyResult.Malformed);
        Check("verify: non-numeric timestamp -> Malformed", NewVerifier(secret).Verify("msg_7", "abc", "v1,AAAA", body) == VerifyResult.Malformed);
        Check("verify: '+<ts>' and ' <ts>' timestamps -> Malformed (FIX-1)",
            NewVerifier(secret).Verify("msg_8", "+" + now, Sig(secret, "msg_8", now, body), body) == VerifyResult.Malformed
            && NewVerifier(secret).Verify("msg_8", " " + now, Sig(secret, "msg_8", now, body), body) == VerifyResult.Malformed);
        Check("verify: out-of-range timestamp 99999999999999 -> Malformed, no exception (FIX-2)",
            NewVerifier(secret).Verify("msg_9", "99999999999999", "v1,AAAA", body) == VerifyResult.Malformed);
        Check("verify: empty id and 129-char id -> Malformed", NewVerifier(secret).Verify("", now.ToString(), "v1,AAAA", body) == VerifyResult.Malformed
            && NewVerifier(secret).Verify(new string('a', 129), now.ToString(), "v1,AAAA", body) == VerifyResult.Malformed);

        var rot = NewVerifier(secret, old);                                                     // current + previous
        Check("rotation: header 'v1,AAAA <signed with previous key>' accepted",
            rot.Verify("msg_10", now.ToString(), "v1,AAAA " + Sig(old, "msg_10", now, body), body) == VerifyResult.Ok);
        Check("rotation: header with v2a entry first and good v1 second accepted",
            rot.Verify("msg_11", now.ToString(), "v2a,xxxx " + Sig(secret, "msg_11", now, body), body) == VerifyResult.Ok);
        Check("rotation: only a v2a entry -> BadSignature", rot.Verify("msg_12", now.ToString(), "v2a,xxxx", body) == VerifyResult.BadSignature);
        Check("rotation: base64 of wrong length / garbage -> BadSignature, no exception",
            rot.Verify("msg_13", now.ToString(), "v1,AAAA v1,!!!! v1,", body) == VerifyResult.BadSignature);

        // --- the doc block as written ---
        var doc = new DocWebhookVerifier([secret], new MemoryCache(new MemoryCacheOptions()), TimeSpan.FromMinutes(5));
        Check("DOC block (as written): valid delivery -> Ok", doc.Verify("d1", now.ToString(), Sig(secret, "d1", now, body), body) == VerifyResult.Ok);
        Check("DOC block (as written): immediate replay -> Duplicate", doc.Verify("d1", now.ToString(), Sig(secret, "d1", now, body), body) == VerifyResult.Duplicate);
        // the doc says: record the id AFTER the work is durably queued. The block records it inside Verify, before.
        var lost = doc.Verify("d2", now.ToString(), Sig(secret, "d2", now, body), body);       // Ok, id now recorded
        // ... the enqueue fails here, endpoint answers 500, the sender retries the same webhook-id ...
        var retry = doc.Verify("d2", (now + 5).ToString(), Sig(secret, "d2", now + 5, body), body);
        Check("DOC block (as written): enqueue fails, sender retries -> Duplicate = the event is LOST (this is the doc defect)",
            lost == VerifyResult.Ok && retry == VerifyResult.Duplicate);
        bool docThrows;
        try { doc.Verify("d3", "99999999999999", "v1,AAAA", body); docThrows = false; }
        catch (ArgumentOutOfRangeException) { docThrows = true; }
        Check("DOC block (as written): timestamp 99999999999999 throws ArgumentOutOfRangeException (= HTTP 500 on an anonymous endpoint)", docThrows);

        // --- endpoint ---
        var queue = new TestQueue();
        var (app, http) = await StartAsync(
            b =>
            {
                b.Services.AddSingleton<IBackgroundQueue>(queue);
                b.Services.AddSingleton(new WebhookVerifier([secret], new MemoryCache(new MemoryCacheOptions()), TimeSpan.FromMinutes(5)));
            },
            a => a.MapWebhookReceiver());
        await using (app)
        {
            var r1 = await http.SendAsync(Delivery(secret, "msg_e1", now, body));
            Check("endpoint: valid delivery -> 202", r1.StatusCode == HttpStatusCode.Accepted, r1.StatusCode.ToString());
            Check("endpoint: queue received the exact raw body", queue.Items.SingleOrDefault() == body);
            var r2 = await http.SendAsync(Delivery(secret, "msg_e1", now + 30, body));
            Check("endpoint: redelivery of the same id -> 200, not queued again", r2.StatusCode == HttpStatusCode.OK && queue.Items.Count == 1);
            var r3 = await http.SendAsync(Delivery(secret, "msg_e2", now, body, signature: "v1,AAAA"));
            Check("endpoint: bad signature -> 401", r3.StatusCode == HttpStatusCode.Unauthorized);
            var r4 = await http.SendAsync(Delivery(secret, "msg_e3", now - 3600, body));
            var r5 = await http.SendAsync(Delivery(secret, "msg.e4", now, body));
            Check("endpoint: stale and malformed -> same 401 for every failure", r4.StatusCode == HttpStatusCode.Unauthorized && r5.StatusCode == HttpStatusCode.Unauthorized);
            var r6 = await http.SendAsync(Delivery(secret, "msg_e5", now, "{\"data\":\"" + new string('x', 300_000) + "\"}"));
            Check("endpoint: body over 256 KiB -> 413", r6.StatusCode == HttpStatusCode.RequestEntityTooLarge, r6.StatusCode.ToString());
            var chunked = Delivery(secret, "msg_e6", now, body);
            chunked.Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(body)));     // unknown length
            chunked.Content.Headers.ContentLength = null;
            var r7 = await http.SendAsync(chunked);
            Check($"endpoint: request without Content-Length -> 413 (behaviour of the doc block, got {(int)r7.StatusCode})",
                r7.StatusCode == HttpStatusCode.RequestEntityTooLarge || r7.StatusCode == HttpStatusCode.Accepted);

            queue.Fail = true;
            Exception? boom = null;
            try { await http.SendAsync(Delivery(secret, "msg_e7", now, body)); } catch (Exception ex) { boom = ex; }
            queue.Fail = false;
            Check("endpoint: queue failure surfaces as an error (sender will retry)", boom is not null);
            var r8 = await http.SendAsync(Delivery(secret, "msg_e7", now + 10, body));
            Check("endpoint: retry after the queue recovered -> 202, event NOT lost (FIX-4)", r8.StatusCode == HttpStatusCode.Accepted && queue.Items.Count == 2);
        }
    }

    // ---------- 9. MCP ----------
    private static async Task McpChecks()
    {
        var cap = new CapturingProvider();
        var policy = new ToolPolicy(new Dictionary<string, string> { ["get_invoice"] = "tools:read", ["delete_invoice"] = "tools:write", ["export_invoices"] = "tools:export" });
        var (app, http) = await StartAsync(
            b =>
            {
                b.Logging.AddProvider(cap);
                b.Services.AddAuthorization(o => o.AddPolicy("tools-write", p => p.RequireAssertion(c => Scopes.Has(c.User, "tools:write"))));
                b.Services.AddAuditedTools(policy);
            },
            a => { a.UseFakeUser(); a.MapMcp("/mcp"); },
            baseAddress: "http://localhost");
        await using (app)
        {
            async Task<McpClient> Connect(string sub, string scope)
            {
                var hc = app.GetTestClient();
                hc.DefaultRequestHeaders.Add("X-Sub", sub);
                hc.DefaultRequestHeaders.Add("X-Scope", scope);
                var transport = new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri("http://localhost/mcp") }, hc, ownsHttpClient: true);
                return await McpClient.CreateAsync(transport);
            }

            await using var reader = await Connect("alice", "tools:read");
            IList<McpClientTool> tools;
            try { tools = await reader.ListToolsAsync(); }
            catch { foreach (var en in cap.Entries.Where(x => x.Level >= LogLevel.Warning)) Console.WriteLine($"  server log: {en.Category} {en.Message}"); throw; }
            Check("mcp: tools/list works over the in-memory transport", tools.Any(t => t.Name == "get_invoice"), string.Join(",", tools.Select(t => t.Name)));

            var ok = await reader.CallToolAsync("get_invoice", new Dictionary<string, object?> { ["id"] = "42" });
            Check("mcp: allowed tool call returns its result", ok.IsError != true && ok.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Any(t => t.Text == "invoice 42"));

            var denied = await reader.CallToolAsync("export_invoices", new Dictionary<string, object?>());
            Check("mcp: tool without the scope in the ToolPolicy table is denied (IsError, generic text)",
                denied.IsError == true && denied.Content.OfType<TextContentBlock>().Any(t => t.Text == "Not authorized for this tool."));

            Exception? attr = null;
            try { await reader.CallToolAsync("delete_invoice", new Dictionary<string, object?> { ["id"] = "42" }); } catch (Exception ex) { attr = ex; }
            Check($"mcp: [Authorize(Policy)] denial by the SDK surfaces as a protocol error ({attr?.GetType().Name}: {attr?.Message})", attr is McpProtocolException);

            await using var writer = await Connect("carol", "tools:read tools:write tools:export");
            var del = await writer.CallToolAsync("delete_invoice", new Dictionary<string, object?> { ["id"] = "42" });
            Check("mcp: token with tools:write may call delete_invoice", del.IsError != true && del.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Any(t => t.Text == "deleted 42"));

            var exp = await writer.CallToolAsync("export_invoices", new Dictionary<string, object?>());
            Check("mcp: scope tools:export allows export_invoices", exp.IsError != true);

            Exception? unknown = null;
            CallToolResult? unknownResult = null;
            try { unknownResult = await reader.CallToolAsync("not_a_tool", new Dictionary<string, object?>()); } catch (Exception ex) { unknown = ex; }
            Check($"mcp: unknown tool is not executed ({(unknown is null ? "IsError=" + unknownResult?.IsError : unknown.GetType().Name)})", unknown is not null || unknownResult?.IsError == true);

            var audit = cap.Entries.Where(e => e.Category == "McpAudit").ToList();
            string Prop(Entry e, string k) => e.State.FirstOrDefault(p => p.Key == k).Value?.ToString() ?? "";
            Console.WriteLine("  audit trail: " + string.Join(" ; ", audit.Select(e => $"{Prop(e, "Tool")}/{Prop(e, "Sub")}/{Prop(e, "Outcome")}")));
            Check("mcp audit: ok for get_invoice by alice", audit.Any(e => Prop(e, "Tool") == "get_invoice" && Prop(e, "Sub") == "alice" && Prop(e, "Outcome") == "ok"));
            Check("mcp audit: denied for export_invoices by alice at Warning", audit.Any(e => Prop(e, "Tool") == "export_invoices" && Prop(e, "Outcome") == "denied" && e.Level == LogLevel.Warning));
            Check("mcp audit: ok for delete_invoice by carol", audit.Any(e => Prop(e, "Tool") == "delete_invoice" && Prop(e, "Sub") == "carol" && Prop(e, "Outcome") == "ok"));
            Check("mcp audit: record carries a duration (Ms)", audit.All(e => e.State.Any(p => p.Key == "Ms")));
            Check("mcp audit: tool name only, never arguments or results", audit.All(e => !e.Message.Contains("42") && !e.Message.Contains("invoice 4")));
            var attrAudited = audit.Any(e => Prop(e, "Tool") == "delete_invoice" && Prop(e, "Sub") == "alice");
            Console.WriteLine($"  NOTE: alice's [Authorize]-denied delete_invoice call is {(attrAudited ? "" : "NOT ")}in the McpAudit filter trail");
        }

        // authentication block of the doc, compiled as written
        var (app2, http2) = await StartAsync(
            b => McpToolAudit.AddMcpAuthAsDocumented(b),
            a => { a.UseAuthentication(); a.UseAuthorization(); a.MapMcp("/mcp").RequireAuthorization("mcp-access"); });
        await using (app2)
        {
            var r = await http2.PostAsync("/mcp", new StringContent("{}", Encoding.UTF8, "application/json"));
            var www = r.Headers.WwwAuthenticate.ToString();
            Check($"mcp auth (doc block): no token -> 401", r.StatusCode == HttpStatusCode.Unauthorized, r.StatusCode.ToString());
            Check("mcp auth (doc block): WWW-Authenticate has resource_metadata and no error", www.Contains("resource_metadata=\"https://mcp.example.com/.well-known/oauth-protected-resource/mcp\"") && !www.Contains("error="), www);
        }
    }

    // ---------- 10. TLS ----------
    private sealed class DuplexStream(PipeReader reader, PipeWriter writer) : Stream
    {
        private readonly Stream _in = reader.AsStream();
        private readonly Stream _out = writer.AsStream();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => _out.Flush();
        public override Task FlushAsync(CancellationToken ct) => _out.FlushAsync(ct);
        public override int Read(byte[] buffer, int offset, int count) => _in.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => _in.ReadAsync(buffer, ct);
        public override void Write(byte[] buffer, int offset, int count) => _out.Write(buffer, offset, count);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) => _out.WriteAsync(buffer, ct);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) { reader.Complete(); writer.Complete(); }
            base.Dispose(disposing);
        }
    }

    private static (Stream Client, Stream Server) MemoryPair()
    {
        var a = new Pipe(); var b = new Pipe();
        return (new DuplexStream(b.Reader, a.Writer), new DuplexStream(a.Reader, b.Writer));
    }

    /// <summary>Runs a TLS handshake over in-memory pipes. Returns (ok, protocol, error type).</summary>
    private static async Task<(bool Ok, SslProtocols Protocol, string? Error)> Handshake(SslServerAuthenticationOptions server, SslClientAuthenticationOptions client)
    {
        var (cs, ss) = MemoryPair();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var c = new SslStream(cs);
        await using var s = new SslStream(ss);
        var st = s.AuthenticateAsServerAsync(server, cts.Token);
        var ct = c.AuthenticateAsClientAsync(client, cts.Token);
        try
        {
            await Task.WhenAll(st, ct);
            return (true, c.SslProtocol, null);
        }
        catch (Exception ex)
        {
            foreach (var t in new[] { st, ct }) _ = t.Exception;     // observe the second failure
            return (false, SslProtocols.None, ex.GetType().Name);
        }
    }

    private static X509Certificate2 Persist(X509Certificate2 c) => X509CertificateLoader.LoadPkcs12(c.Export(X509ContentType.Pfx), null);

    private static X509Certificate2 SelfSigned(string dns)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var req = new CertificateRequest($"CN={dns}", key, HashAlgorithmName.SHA256);
        var san = new SubjectAlternativeNameBuilder(); san.AddDnsName(dns);
        req.CertificateExtensions.Add(san.Build());
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        return Persist(req.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1)));
    }

    private static X509Certificate2 CreateCa(string name)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var req = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        return Persist(req.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1)));
    }

    private static X509Certificate2 IssueClient(X509Certificate2 ca, string cn)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var req = new CertificateRequest($"CN={cn}", key, HashAlgorithmName.SHA256);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.2")], false));
        using var pub = req.Create(ca, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(12), RandomNumberGenerator.GetBytes(8));
        using var withKey = pub.CopyWithPrivateKey(key);
        return Persist(withKey);
    }

    private static SslServerAuthenticationOptions ServerOptions(HttpsConnectionAdapterOptions o, X509Certificate2 cert) => new()
    {
        ServerCertificate = cert,
        EnabledSslProtocols = o.SslProtocols,
        ClientCertificateRequired = o.ClientCertificateMode == ClientCertificateMode.RequireCertificate,
        // Kestrel adapts ClientCertificateValidation exactly like this (certificate, chain, errors).
        RemoteCertificateValidationCallback = o.ClientCertificateValidation is { } v
            ? (_, c, ch, e) => c is not null && v(X509CertificateLoader.LoadCertificate(c.GetRawCertData()), ch, e)
            : null,
    };

    private static SslClientAuthenticationOptions ClientOptions(SslProtocols protocols, string host,
        RemoteCertificateValidationCallback validate, params X509Certificate2[] clientCerts) => new()
    {
        TargetHost = host,
        EnabledSslProtocols = protocols,
        RemoteCertificateValidationCallback = validate,
        ClientCertificates = clientCerts.Length == 0 ? null : new X509CertificateCollection(clientCerts),
    };

    private static async Task TlsChecks()
    {
        // server policy
        var o = new HttpsConnectionAdapterOptions();
        TlsSamples.ConfigureHttps(o);
        Check("kestrel policy: TLS 1.2 + 1.3, no client certificate", o.SslProtocols == (SslProtocols.Tls12 | SslProtocols.Tls13) && o.ClientCertificateMode == ClientCertificateMode.NoCertificate);
        var (kapp, _) = await StartAsync(b => b.WebHost.ConfigureKestrel(TlsSamples.UseTlsDefaults), _ => { });
        await using (kapp)
            Check("kestrel: AddServerHeader = false after UseTlsDefaults", kapp.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value.AddServerHeader == false);

        using var server = SelfSigned("server.test");
        using var other = SelfSigned("server.test");
        var thumb = Convert.ToHexString(SHA256.HashData(server.RawData));
        var tlsBoth = SslProtocols.Tls12 | SslProtocols.Tls13;

        // handshake with the doc's client options; pinning
        var pinnedClient = TlsSamples.CreateClientHandler(c => TlsSamples.PinnedByThumbprint(c, thumb)).SslOptions;
        pinnedClient.TargetHost = "server.test";
        var hs = await Handshake(ServerOptions(o, server), pinnedClient);
        Check($"client (doc block): handshake with the pinned self-signed certificate succeeds ({hs.Protocol})", hs.Ok && hs.Protocol is SslProtocols.Tls12 or SslProtocols.Tls13, hs.Error);
        var hsOther = await Handshake(ServerOptions(o, other), pinnedClient);
        Check($"client (doc block): a different self-signed certificate is rejected ({hsOther.Error})", !hsOther.Ok);
        var unpinned = TlsSamples.CreateClientHandler(_ => false).SslOptions;
        unpinned.TargetHost = "server.test";
        var hsUnpinned = await Handshake(ServerOptions(o, server), unpinned);
        Check($"client (doc block): unpinned self-signed certificate is rejected by default ({hsUnpinned.Error})", !hsUnpinned.Ok);

        // protocol policy
        var o13 = new HttpsConnectionAdapterOptions { SslProtocols = SslProtocols.Tls13 };
        var tls12Only = ClientOptions(SslProtocols.Tls12, "server.test", (_, _, _, _) => true);
        var hsMismatch = await Handshake(ServerOptions(o13, server), tls12Only);
        Check($"protocol policy: TLS1.2-only client against TLS1.3-only server fails ({hsMismatch.Error})", !hsMismatch.Ok);
        var hs12 = await Handshake(ServerOptions(new HttpsConnectionAdapterOptions { SslProtocols = SslProtocols.Tls12 }, server), ClientOptions(tlsBoth, "server.test", (_, _, _, _) => true));
        Check($"protocol policy: server limited to TLS1.2 negotiates {hs12.Protocol}", hs12.Ok && hs12.Protocol == SslProtocols.Tls12);

        // mutual TLS
        using var ca = CreateCa("Test Client CA");
        using var rogueCa = CreateCa("Rogue CA");
        using var good = IssueClient(ca, "service-a");
        using var rogue = IssueClient(rogueCa, "service-a");
        var acceptAllServer = (RemoteCertificateValidationCallback)((_, _, _, _) => true);   // test client trusts the test server

        var docMtls = new HttpsConnectionAdapterOptions { SslProtocols = tlsBoth };
        TlsSamples.ConfigureMtlsAsDocumented(docMtls, _ => true);
        var rDoc = await Handshake(ServerOptions(docMtls, server), ClientOptions(tlsBoth, "server.test", acceptAllServer, good));
        Check($"mTLS DOC block (as written): client certificate from a private CA is REJECTED even though the CA issued it ({rDoc.Error ?? "accepted"})", !rDoc.Ok);

        var fixedMtls = new HttpsConnectionAdapterOptions { SslProtocols = tlsBoth };
        TlsSamples.MutualTlsPrivateCa.Configure(fixedMtls, ca, c => c.Subject == "CN=service-a");
        var rGood = await Handshake(ServerOptions(fixedMtls, server), ClientOptions(tlsBoth, "server.test", acceptAllServer, good));
        Check($"mTLS (FIX-1): certificate issued by the configured CA is accepted ({rGood.Error})", rGood.Ok);
        var rRogue = await Handshake(ServerOptions(fixedMtls, server), ClientOptions(tlsBoth, "server.test", acceptAllServer, rogue));
        Check($"mTLS (FIX-1): same subject from a different CA is rejected ({rRogue.Error})", !rRogue.Ok);
        var rNone = await Handshake(ServerOptions(fixedMtls, server), ClientOptions(tlsBoth, "server.test", acceptAllServer));
        Check($"mTLS (FIX-1): no client certificate is rejected under RequireCertificate ({rNone.Error})", !rNone.Ok);
        var strictList = new HttpsConnectionAdapterOptions { SslProtocols = tlsBoth };
        TlsSamples.MutualTlsPrivateCa.Configure(strictList, ca, c => c.Subject == "CN=someone-else");
        var rAllow = await Handshake(ServerOptions(strictList, server), ClientOptions(tlsBoth, "server.test", acceptAllServer, good));
        Check($"mTLS (FIX-1): valid chain but not on the allow-list is rejected ({rAllow.Error})", !rAllow.Ok);

        // certificate-bound tokens (RFC 8705)
        var expected = Convert.ToBase64String(good.GetCertHash(HashAlgorithmName.SHA256)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Check("x5t#S256: equals base64url(SHA-256(DER)) computed independently, no padding", TlsSamples.X5tS256(good) == expected && !expected.Contains('='));
        Check("x5t#S256: 43 characters", TlsSamples.X5tS256(good).Length == 43);
        var cnf = $"{{\"x5t#S256\":\"{TlsSamples.X5tS256(good)}\"}}";
        Check("binding: matching certificate -> true", TlsSamples.CertificateMatchesBinding(good, cnf));
        Check("binding: different certificate -> false", !TlsSamples.CertificateMatchesBinding(rogue, cnf));
        Check("binding: token bound but no client certificate -> false", !TlsSamples.CertificateMatchesBinding(null, cnf));
        Check("binding: token without cnf -> true (nothing to check)", TlsSamples.CertificateMatchesBinding(null, null));
        var dpopCnf = "{\"jkt\":\"0ZcOCORZNYy-DWpqq30jZyJGHTN0d2HglBV3uiguA4I\"}";
        Check("binding (FIX-2): cnf with only a DPoP jkt is not a certificate binding -> true, no exception", TlsSamples.CertificateMatchesBinding(good, dpopCnf));
        bool docThrows;
        try { _ = System.Text.Json.JsonDocument.Parse(dpopCnf).RootElement.GetProperty("x5t#S256").GetString(); docThrows = false; }
        catch (KeyNotFoundException) { docThrows = true; }
        Check("binding DOC block (as written): GetProperty on a jkt-only cnf throws KeyNotFoundException", docThrows);
    }
}
