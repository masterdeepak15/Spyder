using System.Text.RegularExpressions;

namespace ProductionSamples;

/// <summary>
/// Log injection (CWE-117): an attacker puts CR/LF in a value so one request writes several log lines
/// (fake entries). Neutralise control characters and cap the length of every untrusted value.
/// </summary>
public static class LogSanitizer
{
    public const int MaxLength = 256;

    public static string Clean(string? value, int max = MaxLength)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var src = value.Length > max ? value[..max] : value;
        return string.Create(src.Length, src, static (dest, s) =>
        {
            for (var i = 0; i < s.Length; i++)
                dest[i] = char.IsControl(s[i]) || s[i] is (char)0x2028 or (char)0x2029 ? '_' : s[i];
        });
    }
}

/// <summary>Secret redaction for free text (headers dumped by mistake, exception messages, query strings).</summary>
public static partial class Redactor
{
    public const string Mask = "[REDACTED]";

    [GeneratedRegex(@"\b(bearer|basic)\s+[A-Za-z0-9\-._~+/=]+", RegexOptions.IgnoreCase)]
    private static partial Regex AuthScheme();

    [GeneratedRegex(@"\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]*")]
    private static partial Regex Jwt();

    [GeneratedRegex(@"\b(password|passwd|pwd|secret|client_secret|api[_-]?key|access_token|refresh_token|code_verifier)(\s*[=:]\s*)[^\s&;,""']+", RegexOptions.IgnoreCase)]
    private static partial Regex KeyValueSecret();

    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var t = AuthScheme().Replace(text, m => $"{m.Groups[1].Value} {Mask}");
        t = Jwt().Replace(t, Mask);
        t = KeyValueSecret().Replace(t, m => $"{m.Groups[1].Value}{m.Groups[2].Value}{Mask}");
        return t;
    }
}

/// <summary>
/// LoggerMessage source generator: no boxing, no template parsing per call, template checked at compile time.
/// Always pass untrusted values through Clean / Redact first.
/// </summary>
public static partial class Log
{
    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "request {Method} {Path} query={Query}")]
    public static partial void RequestReceived(this ILogger logger, string method, string path, string query);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Warning, Message = "login failed for {User}")]
    public static partial void LoginFailed(this ILogger logger, string user);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Error, Message = "downstream call failed: {Reason}")]
    public static partial void DownstreamFailed(this ILogger logger, string reason, Exception exception);
}

/// <summary>
/// Takes X-Correlation-Id only if it is a short token of safe characters, else generates one.
/// The id goes to the response, the log scope and the current Activity, so logs, traces and the
/// client's error report can be joined.
/// </summary>
public sealed partial class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string Header = "X-Correlation-Id";
    public const string ItemKey = "CorrelationId";

    [GeneratedRegex("^[A-Za-z0-9_-]{8,64}$")]
    private static partial Regex SafeId();

    public async Task InvokeAsync(HttpContext ctx)
    {
        var incoming = ctx.Request.Headers[Header].ToString();
        var id = SafeId().IsMatch(incoming) ? incoming : Guid.NewGuid().ToString("N");
        ctx.Items[ItemKey] = id;
        ctx.Response.OnStarting(() =>
        {
            ctx.Response.Headers[Header] = id;
            return Task.CompletedTask;
        });
        System.Diagnostics.Activity.Current?.SetTag("correlation.id", id);
        using (logger.BeginScope(new Dictionary<string, object> { [ItemKey] = id }))
        {
            await next(ctx);
        }
    }
}
