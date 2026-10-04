using Nelsar.Axia.Application.Configuration;
using Nelsar.Axia.Domain.ValueObjects;

namespace Nelsar.Axia.Tests;

/// <summary>
/// Locks in the shape of the default UserSearchFilter so a future edit can't silently
/// regress it back to an AD-only filter (sAMAccountName-only) that returns nothing
/// against real OpenLDAP/eDirectory servers. StandardLdapProvider itself talks to a
/// live Novell.Directory.Ldap.LdapConnection with no mockable seam, so the actual
/// conn.Search(...) behavior can only be verified against a real directory (e.g. the
/// OpenLdap/docker-compose.yml dev container) — these tests cover the filter text only.
/// </summary>
public class PortableUserSearchFilterTests
{
    [Theory]
    [InlineData("uid")]
    [InlineData("sAMAccountName")]
    [InlineData("userPrincipalName")]
    [InlineData("mail")]
    public void LdapSettings_DefaultUserSearchFilter_MatchesEveryCommonLoginAttribute(string attribute)
    {
        var filter = new LdapSettings().UserSearchFilter;
        var formatted = string.Format(filter, "jdoe");

        Assert.Contains($"({attribute}=jdoe)", formatted);
    }

    [Theory]
    [InlineData("uid")]
    [InlineData("sAMAccountName")]
    [InlineData("userPrincipalName")]
    [InlineData("mail")]
    public void LdapSearchFilter_Default_MatchesEveryCommonLoginAttribute(string attribute)
    {
        var filter = LdapSearchFilter.Default.Template;
        var formatted = string.Format(filter, "jdoe");

        Assert.Contains($"({attribute}=jdoe)", formatted);
    }

    [Fact]
    public void LdapSettings_DefaultUserSearchFilter_RestrictsToPersonObjectClass()
    {
        var filter = new LdapSettings().UserSearchFilter;

        Assert.StartsWith("(&(objectClass=person)", filter);
    }
}
