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

        var conn = new LdapConnection(options)
        {
            ConnectionTimeout = _settings.ConnectTimeoutSeconds * 1000
        };
        LimitOperations(conn);

        try
        {
            conn.Connect(_settings.Server, port);

            switch (_settings.Security)
            {
                case LdapSecurity.StartTls:
                    try
                    {
                        StartTlsWithinLimit(conn);
                    }
                    catch (LdapException ex) when (ex.ResultCode == LdapException.LdapTimeout)
                    {
                        _logger.LogError(
                            "Directory {Endpoint} accepted the connection but did not answer within {Seconds} seconds, "
                            + "so StartTLS was not attempted and nothing else was sent on it.",
                            endpoint, _settings.OperationTimeoutSeconds);
                        throw;
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

    /// <summary>
    /// Bounds every operation on the connection: the client gives up waiting after
    /// OperationTimeoutSeconds with LdapException.LdapTimeout, and each search asks the server to
    /// stop at the same point. Set through the search constraints because handing those to
    /// Constraints sets the base limits and the search limits together; searches that clone them
    /// to add a paging control keep both.
    /// </summary>
    private void LimitOperations(LdapConnection conn)
    {
        var constraints = conn.SearchConstraints;
        constraints.TimeLimit = _settings.OperationTimeoutSeconds * 1000;
        constraints.ServerTimeLimit = _settings.OperationTimeoutSeconds;
        conn.Constraints = constraints;
    }

    /// <summary>
    /// StartTLS, on a directory that has first shown it is answering.
    ///
    /// LdapConnection.StartTls cannot be given up on. While it waits for the server's answer it
    /// holds the connection's write lock, and both the time limit — which abandons the operation —
    /// and closing the connection need that lock, so a directory that accepted the connection and
    /// never answered held the request thread indefinitely. LDAPS and plain LDAP gave up after a
    /// second in the same test.
    ///
    /// So the directory is asked something first that does keep to the limit: a base read of the
    /// root DSE, requesting no attributes. Any answer will do, a refusal included — it shows there
    /// is a server there. No answer ends in LdapException.LdapTimeout with the lock free, and the
    /// connection closes normally. Nothing is sent but the read: the root DSE is public, and the
    /// credentials only follow the TLS upgrade.
    /// </summary>
    /// <exception cref="LdapException">The directory did not answer in time
    /// (<see cref="LdapException.LdapTimeout"/>), or refused StartTLS.</exception>
    private static void StartTlsWithinLimit(LdapConnection conn)
    {
        try
        {
            var rootDse = conn.Search(string.Empty, LdapConnection.ScopeBase, "(objectClass=*)", ["1.1"], false);
            while (rootDse.HasMore())
                rootDse.Next();
        }
        catch (LdapException ex) when (ex.ResultCode != LdapException.LdapTimeout)
        {
            // An answer, if not the one asked for.
        }

        conn.StartTls();
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
        throw new DomainNotFoundException(ApplicationMessages.UserNotFoundInLdap());
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
