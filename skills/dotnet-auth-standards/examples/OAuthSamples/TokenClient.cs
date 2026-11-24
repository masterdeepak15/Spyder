using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace OAuthSamples;

/// <summary>Token-endpoint calls: client credentials + resource (RFC 6749/8707), exchange (8693), revoke (7009), introspect (7662), device poll (8628).</summary>
public static class TokenClient
{
    /// <summary>client_secret_basic: id and secret are form-urlencoded, then Base64 (RFC 6749 2.3.1 / Appendix B).</summary>
    public static AuthenticationHeaderValue Basic(string clientId, string secret) =>
        new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{WebUtility.UrlEncode(clientId)}:{WebUtility.UrlEncode(secret)}")));

    public static async Task<JsonElement> PostFormAsync(HttpClient http, Uri endpoint, Dictionary<string, string> form,
        AuthenticationHeaderValue? auth, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new FormUrlEncodedContent(form) };
        req.Headers.Authorization = auth;
        using var res = await http.SendAsync(req, ct);
        var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct)).RootElement.Clone();
        if (!res.IsSuccessStatusCode)
        {
            var code = json.TryGetProperty("error", out var e) ? e.GetString() : "unknown";
            throw new OAuthException(code ?? "unknown", (int)res.StatusCode);
        }
        return json;
    }

    public static Dictionary<string, string> ClientCredentialsForm(string scope, string resource) => new()
    {
        ["grant_type"] = "client_credentials",
        ["scope"] = scope,
        ["resource"] = resource          // RFC 8707: ask for a token audience-restricted to this API
    };

    public static Dictionary<string, string> TokenExchangeForm(string subjectToken, string audience) => new()
    {
        ["grant_type"] = "urn:ietf:params:oauth:grant-type:token-exchange",
        ["subject_token"] = subjectToken,
        ["subject_token_type"] = "urn:ietf:params:oauth:token-type:access_token",
        ["requested_token_type"] = "urn:ietf:params:oauth:token-type:access_token",
        ["audience"] = audience
    };

    /// <summary>RFC 7009: 200 means revoked OR unknown token; do not treat the body as meaningful.</summary>
    public static async Task RevokeAsync(HttpClient http, Uri endpoint, string clientId, string secret, string token, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token, ["token_type_hint"] = "refresh_token" })
        };
        req.Headers.Authorization = Basic(clientId, secret);
        using var res = await http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
    }

    /// <summary>RFC 7662: only trust the answer when active == true; anything else is rejected.</summary>
    public static async Task<bool> IsActiveAsync(HttpClient http, Uri endpoint, string rsId, string rsSecret, string token, CancellationToken ct)
    {
        var json = await PostFormAsync(http, endpoint, new() { ["token"] = token, ["token_type_hint"] = "access_token" }, Basic(rsId, rsSecret), ct);
        return json.TryGetProperty("active", out var a) && a.ValueKind == JsonValueKind.True;
    }

    /// <summary>RFC 8628 3.5 polling rules: wait interval (default 5s), slow_down adds 5s, other errors stop.</summary>
    public static async Task<JsonElement> PollDeviceAsync(HttpClient http, Uri tokenEndpoint, string clientId, string deviceCode,
        int? intervalSeconds, TimeSpan lifetime, CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(intervalSeconds ?? 5);
        var deadline = DateTimeOffset.UtcNow + lifetime;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(interval, ct);
            try
            {
                return await PostFormAsync(http, tokenEndpoint, new()
                {
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                    ["device_code"] = deviceCode,
                    ["client_id"] = clientId
                }, null, ct);
            }
            catch (OAuthException ex) when (ex.Error == "authorization_pending") { }
            catch (OAuthException ex) when (ex.Error == "slow_down") { interval += TimeSpan.FromSeconds(5); }
        }
        throw new OAuthException("expired_token", 400);
    }
}

public sealed class OAuthException(string error, int status) : Exception($"OAuth error '{error}' (HTTP {status})")
{
    public string Error { get; } = error;
    public int Status { get; } = status;
}
