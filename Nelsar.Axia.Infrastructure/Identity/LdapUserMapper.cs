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

    public static LdapAttributeSet BuildAttributes(string dn, string name, string email, string password, string baseDn)
    {
        var attrs = new LdapAttributeSet();
        attrs.Add(new LdapAttribute("objectClass", new[] { "top", "person", "organizationalPerson", "inetOrgPerson" }));
        attrs.Add(new LdapAttribute("cn", name));
        attrs.Add(new LdapAttribute("sn", name.Split(' ').LastOrDefault() ?? name));
        attrs.Add(new LdapAttribute("displayName", name));
        attrs.Add(new LdapAttribute("mail", email));
        attrs.Add(new LdapAttribute("uid", Email.Create(email).LocalPart));
        attrs.Add(new LdapAttribute("userPassword", password));
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
