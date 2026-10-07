using System.Collections.Concurrent;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;

namespace Nelsar.Axia.Infrastructure.Identity;

/// <summary>
/// Decides whether a directory's TLS certificate is good enough to send a password to.
///
/// A certificate is accepted when the operating system trusts it for the host name connected to,
/// or — when <c>Ldap:TrustedCaCertificatePath</c> names one — when it chains to that CA and still
/// names the host. A name mismatch is never forgiven: a certificate issued by the right CA to a
/// different server is precisely what an interception looks like. There is deliberately no way to
/// accept anything else; a connection that accepts any certificate protects the password from a
/// passive listener and hands it to an active one.
///
/// One instance per connection, so that a refusal can be told apart from every other way a TLS
/// handshake fails: the connection factory reads <see cref="Rejected"/> to decide whether to say
/// "the certificate" or "the server".
/// </summary>
internal sealed class DirectoryCertificateValidator
{
    private readonly X509Certificate2Collection _trustedAuthorities;
    private readonly string _endpoint;
    private readonly ILogger _logger;

    public DirectoryCertificateValidator(X509Certificate2Collection trustedAuthorities, string endpoint, ILogger logger)
    {
        _trustedAuthorities = trustedAuthorities;
        _endpoint = endpoint;
        _logger = logger;
    }

    /// <summary>Whether this validator has refused a certificate.</summary>
    public bool Rejected { get; private set; }

    /// <summary>A <see cref="RemoteCertificateValidationCallback"/>.</summary>
    public bool Validate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors)
    {
        if (errors == SslPolicyErrors.None)
            return true;

        // Only an untrusted chain is something an extra CA can answer. A wrong name or no
        // certificate at all is refused whatever is configured.
        var chainStatus = string.Empty;
        if (errors == SslPolicyErrors.RemoteCertificateChainErrors
            && certificate is not null
            && _trustedAuthorities.Count > 0
            && ChainsToTrustedAuthority(certificate, chain, out chainStatus))
        {
            return true;
        }

        Rejected = true;

        // Error, because nothing will work until someone acts on it, and with everything needed
        // to do so: which server, what was wrong, and which certificate it was. None of it is
        // secret — it is what the server shows anyone who connects.
        _logger.LogError(
            "Refused the TLS certificate of directory {Endpoint}: {PolicyErrors}. Subject {Subject}, issuer {Issuer}, "
            + "thumbprint {Thumbprint}, valid {NotBefore:u} to {NotAfter:u}.{ChainStatus}",
            _endpoint,
            errors,
            certificate?.Subject ?? "(none presented)",
            certificate?.Issuer,
            (certificate as X509Certificate2)?.Thumbprint ?? certificate?.GetCertHashString(),
            (certificate as X509Certificate2)?.NotBefore,
            (certificate as X509Certificate2)?.NotAfter,
            string.IsNullOrEmpty(chainStatus) ? Describe(chain) : chainStatus);

        return false;
    }

    private bool ChainsToTrustedAuthority(X509Certificate certificate, X509Chain? presented, out string status)
    {
        using var leaf = new X509Certificate2(certificate);
        using var chain = new X509Chain();

        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.AddRange(_trustedAuthorities);
        // The same as the LDAP library's own default, so that trusting a CA here changes which
        // roots count and nothing else. A revocation check would also need the CA's CRL endpoint
        // to be reachable from wherever this runs, which an internal CA's often is not.
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

        // Intermediates the server sent, so a CA that issues through one still chains.
        if (presented is not null)
        {
            foreach (var element in presented.ChainElements)
                chain.ChainPolicy.ExtraStore.Add(element.Certificate);
        }

        var built = chain.Build(leaf);
        status = built ? string.Empty : " Chain against the configured CA: " + Describe(chain);
        return built;
    }

    private static string Describe(X509Chain? chain) =>
        chain is null || chain.ChainStatus.Length == 0
            ? string.Empty
            : " " + string.Join("; ", chain.ChainStatus.Select(s => $"{s.Status}: {s.StatusInformation.Trim()}"));
}

/// <summary>
/// Reads the certificate authorities named by <c>Ldap:TrustedCaCertificatePath</c>.
///
/// Read once per path and kept: every connection to the directory consults them, and re-reading a
/// file per sign-in buys nothing but disk reads. Replacing the CA therefore takes a restart, which
/// is also when the startup check below gets to look at the new file.
/// </summary>
internal static class TrustedCertificateAuthorities
{
    private static readonly ConcurrentDictionary<string, X509Certificate2Collection> Loaded = new(StringComparer.Ordinal);

    /// <summary>The certificates in the file, or an empty collection when no path is set.</summary>
    /// <exception cref="InvalidOperationException">The file cannot be read or holds no certificate.</exception>
    public static X509Certificate2Collection Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return [];

        return Loaded.GetOrAdd(path.Trim(), Read);
    }

    private static X509Certificate2Collection Read(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            var certificates = new X509Certificate2Collection();

            // PEM may hold a bundle — a root and its intermediates — so it is imported as a
            // collection. Anything else is taken to be one DER certificate.
            if (System.Text.Encoding.ASCII.GetString(bytes).Contains("-----BEGIN CERTIFICATE-----", StringComparison.Ordinal))
                certificates.ImportFromPemFile(path);
            else
                certificates.Add(X509CertificateLoader.LoadCertificate(bytes));

            if (certificates.Count == 0)
                throw new InvalidOperationException($"'{path}' contains no certificate.");

            return certificates;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            throw new InvalidOperationException(
                $"Ldap:TrustedCaCertificatePath is '{path}', which could not be read as a PEM or DER certificate: {ex.Message}",
                ex);
        }
    }
}
