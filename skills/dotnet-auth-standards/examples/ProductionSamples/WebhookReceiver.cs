using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;

namespace ProductionSamples;

public enum VerifyResult { Ok, Malformed, StaleTimestamp, BadSignature, Duplicate }

public interface IBackgroundQueue
{
    ValueTask EnqueueAsync(string rawBody);
}

/// <summary>
/// Standard Webhooks v1 (symmetric) receiver logic: signature over "id.timestamp.body", timestamp window in
/// both directions, secret rotation list, replay/idempotency by webhook-id.
/// Differences from the block in reference/webhooks-events.md section 4.2 are marked FIX-n.
/// </summary>
public sealed class WebhookVerifier(IReadOnlyList<byte[]> secrets, IMemoryCache seen, TimeSpan tolerance, TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public VerifyResult Verify(string id, string timestamp, string signatureHeader, string rawBody)
    {
        if (id.Length is 0 or > 128 || id.Contains('.')) return VerifyResult.Malformed;

        // FIX-1: NumberStyles.None + invariant culture. long.TryParse(string) also accepts "+5", " 5 " and "-5";
        // the signed text must be exactly the header text, so only plain digits are accepted.
        if (!long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var ts)) return VerifyResult.Malformed;

        // FIX-2: FromUnixTimeSeconds throws ArgumentOutOfRangeException for ts > 253402300799; an attacker header
        // such as "99999999999999" would be an unhandled exception (500) on an anonymous endpoint.
        if (ts is < 0 or > 253_402_300_799) return VerifyResult.Malformed;

        var age = _clock.GetUtcNow() - DateTimeOffset.FromUnixTimeSeconds(ts);
        if (age > tolerance || age < -tolerance) return VerifyResult.StaleTimestamp;           // both directions

        var content = Encoding.UTF8.GetBytes($"{id}.{ts.ToString(CultureInfo.InvariantCulture)}.{rawBody}");
        var ok = false;
        Span<byte> candidate = stackalloc byte[32];                                             // FIX-3: hoisted out of the loop (CA2014)
        foreach (var secret in secrets)                                                         // current + previous secret
        {
            var expected = HMACSHA256.HashData(secret, content);
            foreach (var part in signatureHeader.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!part.StartsWith("v1,", StringComparison.Ordinal)) continue;               // ignore v2a etc. here
                if (Convert.TryFromBase64String(part[3..], candidate, out var n) && n == 32
                    && CryptographicOperations.FixedTimeEquals(candidate, expected))
                    ok = true;                                                                  // no early exit
            }
        }
        if (!ok) return VerifyResult.BadSignature;

        // FIX-4: this method only CHECKS for a replay. The id is recorded by MarkProcessed AFTER the event is durably
        // queued. The doc block recorded it here, before the work: if the enqueue then failed, the sender's retry
        // (same webhook-id) answered 200 Duplicate and the event was lost, which contradicts the doc's own advice.
        return seen.TryGetValue(id, out _) ? VerifyResult.Duplicate : VerifyResult.Ok;
    }

    public void MarkProcessed(string id) => seen.Set(id, true, tolerance * 2);
}

public static class WebhookSigner
{
    public static string Sign(byte[] secret, string id, long unixSeconds, string body)
        => "v1," + Convert.ToBase64String(HMACSHA256.HashData(secret,
            Encoding.UTF8.GetBytes($"{id}.{unixSeconds.ToString(CultureInfo.InvariantCulture)}.{body}")));

    /// <summary>"whsec_" + base64 -> key bytes. Use the decoded bytes as the HMAC key, not the string.</summary>
    public static byte[] ParseSecret(string whsec) =>
        whsec.StartsWith("whsec_", StringComparison.Ordinal) ? Convert.FromBase64String(whsec[6..])
        : throw new FormatException("Secret must start with whsec_.");
}

public static class WebhookEndpoint
{
    public const int MaxBodyBytes = 262_144;

    public static RouteHandlerBuilder MapWebhookReceiver(this IEndpointRouteBuilder app) =>
        app.MapPost("/webhooks/billing", async (HttpRequest req, WebhookVerifier verifier, IBackgroundQueue queue) =>
        {
            // The doc block returned 413 for ANY request without Content-Length (chunked uploads) as well as for big ones.
            // Behaviour kept (a signed webhook has a known length), but the read below is also capped, so a lying
            // Content-Length cannot make the server buffer more than the limit.
            if (req.ContentLength is null or > MaxBodyBytes) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

            using var ms = new MemoryStream();
            await req.Body.CopyToAsync(ms, req.HttpContext.RequestAborted);
            if (ms.Length > MaxBodyBytes) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            var raw = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);

            var id = req.Headers["webhook-id"].ToString();
            var result = verifier.Verify(id, req.Headers["webhook-timestamp"].ToString(),
                req.Headers["webhook-signature"].ToString(), raw);

            switch (result)
            {
                case VerifyResult.Ok:
                    await queue.EnqueueAsync(raw);
                    verifier.MarkProcessed(id);                  // only after the enqueue succeeded
                    return Results.Accepted();                   // 202: acknowledge fast
                case VerifyResult.Duplicate:
                    return Results.Ok();                         // already handled: success, not error
                default:
                    return Results.Unauthorized();               // same answer for every failure
            }
        });
}
