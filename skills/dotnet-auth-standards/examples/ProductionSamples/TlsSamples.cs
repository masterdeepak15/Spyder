using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;

namespace ProductionSamples;

/// <summary>Blocks of reference/transport-tls.md, compiled. Differences from the doc blocks are marked FIX-n.</summary>
public static class TlsSamples
{
    // ---- server: explicit protocol policy (doc section 3) ----
    public static void ConfigureHttps(HttpsConnectionAdapterOptions o)
    {
        o.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;      // default None = OS decides
        o.ClientCertificateMode = ClientCertificateMode.NoCertificate;
    }

    public static void UseTlsDefaults(KestrelServerOptions k)
    {
        k.AddServerHeader = false;
        k.ConfigureHttpsDefaults(ConfigureHttps);
    }

    // ---- client: protocol policy + pinning (doc section 4) ----
    public static SocketsHttpHandler CreateClientHandler(Func<X509Certificate2, bool> isPinned) => new()
    {
        SslOptions = new SslClientAuthenticationOptions
        {
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            // The callback must say "no" by default. "errors == None" keeps normal CA validation; the pin is the exception.
            RemoteCertificateValidationCallback = (_, cert, _, errors) =>
                errors == SslPolicyErrors.None || (cert is not null && isPinned(X509CertificateLoader.LoadCertificate(cert.GetRawCertData()))),
        },
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),             // pick up DNS and certificate rotation
    };

    public static bool PinnedByThumbprint(X509Certificate2 cert, string sha256HexThumbprint) =>
        CryptographicOperations.FixedTimeEquals(SHA256.HashData(cert.RawData), Convert.FromHexString(sha256HexThumbprint));

    // ---- server mTLS (doc section 5) ----
    /// <summary>
    /// As the doc block: RequireCertificate plus "chain is not null &amp;&amp; errors == None &amp;&amp; IsTrustedClient".
    /// PITFALL (tested): when clients hold certificates from a PRIVATE CA that is not in the OS trust store,
    /// errors is never None, so this callback rejects every client. See <see cref="MutualTlsPrivateCa"/>.
    /// </summary>
    public static void ConfigureMtlsAsDocumented(HttpsConnectionAdapterOptions o, Func<X509Certificate2, bool> isTrustedClient)
    {
        o.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
        o.ClientCertificateValidation = (cert, chain, errors) =>
            chain is not null && errors == SslPolicyErrors.None && isTrustedClient(cert);
    }

    /// <summary>FIX-1: validate the client chain against YOUR CA only (custom root trust), then apply the allow-list.</summary>
    public static class MutualTlsPrivateCa
    {
        public static void Configure(HttpsConnectionAdapterOptions o, X509Certificate2 clientCa, Func<X509Certificate2, bool> isTrustedClient)
        {
            o.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
            o.ClientCertificateValidation = (cert, _, _) => Validate(cert, clientCa) && isTrustedClient(cert);
        }

        public static bool Validate(X509Certificate2 cert, X509Certificate2 clientCa)
        {
            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(clientCa);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;           // set Online + a CRL/OCSP plan in production
            chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.2"));   // clientAuth EKU
            return chain.Build(cert);
        }
    }

    // ---- certificate-bound access token (RFC 8705, doc section 6) ----
    public static string X5tS256(X509Certificate2 cert)
        => Convert.ToBase64String(SHA256.HashData(cert.RawData)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>
    /// Doc block, as a function. cnf is the claim value (JSON). Returns false when the token is bound to a different
    /// certificate or is bound and no certificate was presented; true when unbound or matching.
    /// </summary>
    public static bool CertificateMatchesBinding(X509Certificate2? cert, string? cnf)
    {
        // FIX-2: TryGetProperty. The doc block used GetProperty, which throws KeyNotFoundException (a 500) when cnf
        // exists but carries another confirmation member, for example a DPoP "jkt".
        string? bound = null;
        if (cnf is not null && JsonDocument.Parse(cnf).RootElement.TryGetProperty("x5t#S256", out var v)) bound = v.GetString();
        return !(bound is not null && (cert is null || !CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(bound), Encoding.ASCII.GetBytes(X5tS256(cert)))));
    }
}
