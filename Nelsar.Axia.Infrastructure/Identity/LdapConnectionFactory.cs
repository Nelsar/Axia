using Novell.Directory.Ldap;
using Microsoft.Extensions.Options;
using Nelsar.Axia.Application.Common;
using Nelsar.Axia.Application.Configuration;
using Nelsar.Axia.Application.Resources;
using Nelsar.Axia.Domain.Exceptions;
using Nelsar.Axia.Domain.ValueObjects;

namespace Nelsar.Axia.Infrastructure.Identity;

public class LdapConnectionFactory
{
    private readonly LdapSettings _settings;

    public LdapConnectionFactory(IOptions<LdapSettings> options)
    {
        _settings = options.Value;
    }

    public LdapConnection CreateAdminConnection()
    {
        var conn = new LdapConnection();
        conn.Connect(_settings.Server, _settings.Port);
        conn.Bind(_settings.AdminUsername, _settings.AdminPassword);
        return conn;
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
        var conn = new LdapConnection();
        conn.Connect(_settings.Server, _settings.Port);
        var userDn = FindUserDn(conn, username);
        conn.Bind(userDn, password);
        return conn;
    }

    /// <summary>
    /// Builds a DN for a login directly, without searching. The value is escaped per RFC 4514 —
    /// an unescaped comma in the name would otherwise end the RDN early and let the rest of the
    /// DN be dictated by whoever supplied the login.
    /// </summary>
    public string BuildUserDn(string username) =>
        $"CN={LdapDistinguishedNameEscaper.Escape(username)},{_settings.BaseDn}";
}
