using Nelsar.Axia.Infrastructure.Identity.Providers;

namespace Nelsar.Axia.Tests;

/// <summary>
/// Which groups an account is in, when the directory does not say so on the account.
///
/// Membership was read from memberOf alone, which only Active Directory and an OpenLDAP with the
/// memberof overlay provide. Elsewhere every account was in no groups: administrators signed in
/// as plain users, and — the part that does not fail safe — nobody was recognised as a member of
/// a protected group. The groups are now also asked directly; these pin how.
/// </summary>
public class GroupMembershipTests
{
    private const string Dn = "uid=ivan.petrov,ou=people,dc=example,dc=com";

    [Theory]
    [InlineData("(&(objectClass=groupOfNames)(member=uid=ivan.petrov,ou=people,dc=example,dc=com))")]
    [InlineData("(&(objectClass=group)(member=uid=ivan.petrov,ou=people,dc=example,dc=com))")]
    [InlineData("(&(objectClass=groupOfUniqueNames)(uniqueMember=uid=ivan.petrov,ou=people,dc=example,dc=com))")]
    [InlineData("(&(objectClass=posixGroup)(memberUid=ivan.petrov))")]
    public void EveryShapeOfMembership_IsAskedFor(string clause)
    {
        var filter = StandardLdapProvider.GroupMembershipFilter(Dn, "ivan.petrov");

        Assert.StartsWith("(|", filter);
        Assert.Contains(clause, filter);
    }

    [Fact]
    public void WithoutALogin_PosixGroupsAreNotAskedFor()
    {
        // memberUid holds a login, and an empty one would match every group with an empty
        // memberUid — or, written as a presence test by mistake, every posixGroup there is.
        Assert.DoesNotContain("posixGroup", StandardLdapProvider.GroupMembershipFilter(Dn, uid: null));
        Assert.DoesNotContain("posixGroup", StandardLdapProvider.GroupMembershipFilter(Dn, uid: "  "));
    }

    [Fact]
    public void ADnWithFilterCharactersInIt_IsEscaped_SoItCannotWidenTheSearch()
    {
        // A name in parentheses, or with a star in it, is a legal RDN. Unescaped, ")" ends the
        // clause and "*" turns an equality into a substring match — membership of groups that
        // do not name this entry at all.
        var filter = StandardLdapProvider.GroupMembershipFilter(
            "cn=Ivan (IT)*,ou=people,dc=example,dc=com", "ivan*");

        Assert.Contains(@"member=cn=Ivan \28IT\29\2a,ou=people,dc=example,dc=com", filter);
        Assert.Contains(@"memberUid=ivan\2a", filter);
        Assert.DoesNotContain("(IT)", filter);
    }

    [Fact]
    public void TheSameGroupFromBothSources_IsListedOnce()
    {
        // memberOf and a search can spell one DN differently.
        var merged = StandardLdapProvider.MergeGroups(
            ["CN=Administrators,OU=Groups,DC=example,DC=com"],
            ["cn=administrators,ou=groups,dc=example,dc=com", "cn=Editors,ou=groups,dc=example,dc=com"]);

        Assert.Equal(
            ["CN=Administrators,OU=Groups,DC=example,DC=com", "cn=Editors,ou=groups,dc=example,dc=com"],
            merged);
    }

    [Fact]
    public void GroupsOnlyTheSearchFound_AreKept()
    {
        // The case this exists for: no memberOf at all.
        var merged = StandardLdapProvider.MergeGroups([], ["cn=Administrators,ou=groups,dc=example,dc=com"]);

        Assert.Equal(["cn=Administrators,ou=groups,dc=example,dc=com"], merged);
    }
}
