using System.Collections.Concurrent;
using Novell.Directory.Ldap;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nelsar.Axia.Application.Common;
using Nelsar.Axia.Application.Configuration;
using Nelsar.Axia.Application.Resources;
using Nelsar.Axia.Contracts.Exceptions;
using Nelsar.Axia.Domain.Exceptions;
using Nelsar.Axia.Domain.ValueObjects;

namespace Nelsar.Axia.Infrastructure.Identity;

public class LdapConnectionFactory
{
    /// <summary>Endpoints already warned about, so a plain-LDAP directory says so once per process
    /// rather than once per request.</summary>
    private static readonly ConcurrentDictionary<string, byte> PlaintextWarned = new(StringComparer.OrdinalIgnoreCase);

    private readonly LdapSettings _settings;
    private readonly ILogger _logger;

    public LdapConnectionFactory(IOptions<LdapSettings> options, ILogger<LdapConnectionFactory>? logger = null)
    {
        _settings = options.Value;
        _logger = logger ?? (ILogger)NullLogger.Instance;
    }

    public LdapConnection CreateAdminConnection()
    {
        var conn = Open();
        try
        {
            conn.Bind(_settings.AdminUsername, _settings.AdminPassword);
            return conn;
        }
        catch
        {
            conn.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Opens a connection to the configured server, protected as <see cref="LdapSettings.Security"/>
    /// says, and not yet bound. Every connection this application makes to a directory comes from
    /// here, so that none of them can skip the TLS — the user's own bind when signing in included.
    ///
    /// StartTLS that does not take is a failure, not a reason to carry on in plain text: carrying
    /// on is exactly the downgrade an attacker would arrange by stripping the server's answer.
    ///
    /// An empty server used to reach the socket layer as-is and come back as an ArgumentException
    /// from DnsEndPoint — a 500, "an unexpected error occurred", on every sign-in to a deployment
    /// that keeps its directories in the database and leaves Ldap:Server unset. It is a directory
    /// that cannot be used, and is reported as one.
    /// </summary>
    /// <exception cref="DirectoryUnavailableException">
    /// No server is configured, or the directory's certificate was refused.
    /// </exception>
    /// <exception cref="LdapException">The directory could not be reached or StartTLS failed.</exception>
    public LdapConnection Open()
    {
        if (string.IsNullOrWhiteSpace(_settings.Server))
            throw new DirectoryUnavailableException(ApplicationMessages.DirectoryServerNotConfigured());

        var port = _settings.ResolvedPort;
        var endpoint = $"{_settings.Server}:{port}";
        var validator = new DirectoryCertificateValidator(
            TrustedCertificateAuthorities.Load(_settings.TrustedCaCertificatePath), endpoint, _logger);

        var options = new LdapConnectionOptions()
            .ConfigureRemoteCertificateValidationCallback(validator.Validate);
        if (_settings.Security == LdapSecurity.Ldaps)
            options.UseSsl();

        var conn = new LdapConnection(options);
        try
        {
            conn.Connect(_settings.Server, port);

            switch (_settings.Security)
            {
                case LdapSecurity.StartTls:
                    try
                    {
                        conn.StartTls();
                    }
                    catch (LdapException ex) when (!validator.Rejected)
                    {
                        // The directory's own answer — "unsupported extended operation" — says
                        // nothing about TLS to whoever reads it in the log, so this line does.
                        _logger.LogError(ex,
                            "Directory {Endpoint} did not accept StartTLS, so the connection was closed before "
                            + "anything was sent on it. Enable TLS on the directory, or use Ldaps.",
                            endpoint);
                        throw;
                    }

                    if (!conn.Tls)
                        throw new LdapException("StartTLS did not take effect.", LdapException.ConnectError, endpoint);
                    break;

                case LdapSecurity.None:
                    WarnPlaintextOnce(endpoint);
                    break;
            }

            return conn;
        }
        catch (Exception ex) when (validator.Rejected)
        {
            conn.Dispose();
            throw new DirectoryUnavailableException(ApplicationMessages.DirectoryCertificateRejected(), ex);
        }
        catch
        {
            conn.Dispose();
            throw;
        }
    }

    private void WarnPlaintextOnce(string endpoint)
    {
        if (!PlaintextWarned.TryAdd(endpoint, 0))
            return;

        _logger.LogWarning(
            "Directory {Endpoint} is reached over plain LDAP because its security is set to None. Every bind "
            + "crosses the network unencrypted, the passwords of users signing in included. Use Ldaps or StartTls.",
            endpoint);
    }

    /// <summary>
    /// Finds the DN to bind as for a given login.
    /// </summary>
    /// <exception cref="DomainNotFoundException">No entry in the directory matches the login.</exception>
    public string FindUserDn(LdapConnection conn, string username)
    {
        // The login went into the filter template raw. A login of
        // `*)(objectClass=*` closed the intended clause and appended one of the caller's
        // choosing — the search then matched the first entry in the tree, and the DN it returned
        // is what CreateUserConnection binds as. StandardLdapProvider.SearchOne escapes for
        // exactly this reason; this path, which is the other way a filter gets built, did not.
        var escaped = LdapFilterEscaper.Escape(username);
        var searchFilter = LdapSearchFilter.FromStorage(_settings.UserSearchFilter).Format(escaped);

        var searchResults = conn.Search(
            _settings.BaseDn,
            LdapConnection.ScopeSub,
            searchFilter,
            null,
            false
        );

        if (searchResults.HasMore())
        {
            var entry = searchResults.Next();
            return entry.Dn;
        }

        // Was `throw new Exception(...)`, which GlobalExceptionHandler could only read as a bug
        // and answer with a 500. A login that names nobody is a normal outcome, not a fault.
        throw new DomainNotFoundException(ApplicationMessages.UserNotFoundInLdap(username));
    }

    public LdapConnection CreateUserConnection(string username, string password)
    {
        var conn = Open();
        try
        {
            var userDn = FindUserDn(conn, username);
            conn.Bind(userDn, password);
            return conn;
        }
        catch
        {
            conn.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Builds a DN for a login directly, without searching. The value is escaped per RFC 4514 —
    /// an unescaped comma in the name would otherwise end the RDN early and let the rest of the
    /// DN be dictated by whoever supplied the login.
    /// </summary>
    public string BuildUserDn(string username) =>
        $"CN={LdapDistinguishedNameEscaper.Escape(username)},{_settings.BaseDn}";
}
