namespace ProductionSamples;

/// <summary>What happened, who did it, to what, with what result. No secrets, no raw PII.</summary>
public sealed record AuditEvent(string Action, string Actor, string Resource, string Outcome,
    string? Email = null, string? ClientIp = null, string? CorrelationId = null);

public interface IAuditLog
{
    void Write(AuditEvent e);
}

/// <summary>Masking helpers for personal data that must be traceable but not readable.</summary>
public static class Pii
{
    /// <summary>alice@example.com -> a***@example.com. Keeps the domain for triage, hides the person.</summary>
    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "";
        var at = email.IndexOf('@');
        return at <= 0 ? "***" : email[0] + "***" + email[at..];
    }

    /// <summary>4111111111111111 -> ************1111. Never log more than the last 4.</summary>
    public static string MaskKeepLast4(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Length <= 4 ? new string('*', value.Length) : new string('*', value.Length - 4) + value[^4..];
    }

    /// <summary>IPv4 203.0.113.77 -> 203.0.113.0 (truncation); IPv6 keeps the first 3 groups.</summary>
    public static string TruncateIp(string? ip)
    {
        if (string.IsNullOrEmpty(ip)) return "";
        if (ip.Contains(':')) return string.Join(':', ip.Split(':').Take(3)) + "::";
        var p = ip.Split('.');
        return p.Length == 4 ? $"{p[0]}.{p[1]}.{p[2]}.0" : "***";
    }
}

/// <summary>
/// Writes audit events through its OWN logger category ("Audit"), so it can be routed to a separate sink,
/// kept at Information even when the app category is Warning, and retained longer.
/// Configure: "Logging": { "LogLevel": { "Audit": "Information" } } plus a provider or filter that sends "Audit" to the audit store.
/// </summary>
public sealed partial class AuditLogger(ILoggerFactory factory) : IAuditLog
{
    public const string Category = "Audit";
    private readonly ILogger _log = factory.CreateLogger(Category);

    public void Write(AuditEvent e) => Event(
        _log,
        LogSanitizer.Clean(e.Action),
        LogSanitizer.Clean(e.Actor),
        LogSanitizer.Clean(e.Resource),
        LogSanitizer.Clean(e.Outcome),
        Pii.MaskEmail(e.Email),
        Pii.TruncateIp(e.ClientIp),
        LogSanitizer.Clean(e.CorrelationId));

    [LoggerMessage(EventId = 9000, Level = LogLevel.Information,
        Message = "audit action={Action} actor={Actor} resource={Resource} outcome={Outcome} email={Email} ip={Ip} correlation={CorrelationId}")]
    private static partial void Event(ILogger logger, string action, string actor, string resource, string outcome,
        string email, string ip, string correlationId);
}
