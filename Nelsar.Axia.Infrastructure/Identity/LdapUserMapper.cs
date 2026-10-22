using Novell.Directory.Ldap;
using Nelsar.Axia.Domain.ValueObjects;
using Nelsar.Axia.Contracts.Common;

namespace Nelsar.Axia.Infrastructure.Identity;

public static class LdapUserMapper
{
    public static UserDto ToDto(LdapEntry entry)
    {
        var attrs = entry.GetAttributeSet();

        return new UserDto
        {
            Id = Guid.NewGuid(),
            Email = GetAttr(attrs, "mail"),
            Name = GetAttr(attrs, "displayName", "cn"),
            Role = Application.Configuration.UserRoles.User
        };
    }

    /// <summary>
    /// The entry for a new account — without a password, which is set afterwards through the
    /// directory's own mechanism so that it is hashed and checked (see DirectoryPasswords).
    /// </summary>
    public static LdapAttributeSet BuildAttributes(string name, string email)
    {
        var attrs = new LdapAttributeSet();
        attrs.Add(new LdapAttribute("objectClass", new[] { "top", "person", "organizationalPerson", "inetOrgPerson" }));
        attrs.Add(new LdapAttribute("cn", name));
        attrs.Add(new LdapAttribute("sn", name.Split(' ').LastOrDefault() ?? name));
        attrs.Add(new LdapAttribute("displayName", name));
        attrs.Add(new LdapAttribute("mail", email));
        attrs.Add(new LdapAttribute("uid", Email.Create(email).LocalPart));
        return attrs;
    }

    /// <summary>
    /// The same account as Active Directory has it: a <c>user</c>, which is what makes it a
    /// security principal with a SID that Windows signs in, its login in sAMAccountName rather
    /// than uid. Written without a password and without userAccountControl, so the directory
    /// creates it switched off; it is switched on once it has a password.
    ///
    /// <see cref="BuildAttributes"/> wrote an inetOrgPerson named by uid=, which Active Directory
    /// refused outright — its inetOrgPerson is named by cn — so creating an account through the
    /// cross-platform provider never worked there.
    /// </summary>
    public static LdapAttributeSet BuildActiveDirectoryAttributes(string name, string email)
    {
        var attrs = new LdapAttributeSet();
        attrs.Add(new LdapAttribute("objectClass", new[] { "top", "person", "organizationalPerson", "user" }));
        attrs.Add(new LdapAttribute("cn", name));
        attrs.Add(new LdapAttribute("sn", name.Split(' ').LastOrDefault() ?? name));
        attrs.Add(new LdapAttribute("displayName", name));
        attrs.Add(new LdapAttribute("mail", email));
        attrs.Add(new LdapAttribute("sAMAccountName", Email.Create(email).LocalPart));
        return attrs;
    }

    /// <summary>Characters sAMAccountName may not hold, in addition to control characters.</summary>
    private static readonly char[] ForbiddenInActiveDirectoryName = ['"', '/', '\\', '[', ']', ':', ';', '|', '=', ',', '+', '*', '?', '<', '>', '@'];

    /// <summary>
    /// Whether a login can be a user's sAMAccountName: 20 characters at most, none of the
    /// forbidden ones, and not dots or spaces alone. Checked before anything is written, because
    /// Active Directory's own refusal names none of this.
    /// </summary>
    public static bool IsActiveDirectoryAccountName(string login) =>
        login.Length is > 0 and <= 20
        && login.IndexOfAny(ForbiddenInActiveDirectoryName) < 0
        && !login.Any(char.IsControl)
        && login.Trim('.', ' ').Length > 0;

    private static string GetAttr(LdapAttributeSet attrs, params string[] names)
    {
        foreach (var name in names)
        {
            if (attrs.TryGetValue(name, out var attr))
            {
                return attr.StringValue ?? string.Empty;
            }
        }
        return string.Empty;
    }
}
