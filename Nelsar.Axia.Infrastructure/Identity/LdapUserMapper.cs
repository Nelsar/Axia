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
