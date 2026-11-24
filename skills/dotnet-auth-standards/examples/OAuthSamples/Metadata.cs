namespace OAuthSamples;

/// <summary>Well-known URI construction (RFC 8414 3.1, RFC 9728 3.1) and redirect URI matching (RFC 9700 4.1.3).</summary>
public static class Metadata
{
    /// <summary>Insert /.well-known/{suffix} between host and path; strip a terminating slash first.</summary>
    public static Uri WellKnown(Uri identifier, string suffix)
    {
        if (identifier.Scheme != Uri.UriSchemeHttps) throw new ArgumentException("identifier must be https", nameof(identifier));
        var path = identifier.AbsolutePath.TrimEnd('/');
        return new UriBuilder(identifier.Scheme, identifier.Host, identifier.IsDefaultPort ? -1 : identifier.Port,
            $"/.well-known/{suffix}{path}").Uri;
    }

    /// <summary>OIDC Discovery 4.1 APPENDS to the issuer (https://host/issuer1 -> https://host/issuer1/.well-known/openid-configuration); RFC 8414 INSERTS. Do not mix them up.</summary>
    public static Uri OidcConfiguration(Uri issuer)
    {
        if (issuer.Scheme != Uri.UriSchemeHttps) throw new ArgumentException("issuer must be https", nameof(issuer));
        var path = issuer.AbsolutePath.TrimEnd('/');
        return new UriBuilder(issuer.Scheme, issuer.Host, issuer.IsDefaultPort ? -1 : issuer.Port, $"{path}/.well-known/openid-configuration").Uri;
    }

    /// <summary>The issuer/resource in the document must be identical to the identifier used to build the URL; else discard it.</summary>
    public static bool IdentifierMatches(string expected, string? fromDocument) =>
        string.Equals(expected, fromDocument, StringComparison.Ordinal);

    /// <summary>Exact string match, except any port is allowed for loopback IP redirects of native apps (RFC 9700 2.1 / 8.4.2).</summary>
    public static bool RedirectUriAllowed(string requested, IEnumerable<string> registered)
    {
        foreach (var r in registered)
        {
            if (string.Equals(r, requested, StringComparison.Ordinal)) return true;
            if (Uri.TryCreate(r, UriKind.Absolute, out var reg) && Uri.TryCreate(requested, UriKind.Absolute, out var req)
                && reg.Scheme == Uri.UriSchemeHttp && req.Scheme == Uri.UriSchemeHttp
                && reg.IsLoopback && req.IsLoopback
                && reg.Host == req.Host && (reg.Host is "127.0.0.1" or "[::1]")
                && reg.AbsolutePath == req.AbsolutePath && reg.Query == req.Query)
                return true;
        }
        return false;
    }
}
