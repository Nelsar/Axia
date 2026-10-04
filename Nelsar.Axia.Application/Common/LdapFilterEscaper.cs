namespace Nelsar.Axia.Application.Common;

// RFC 4515 escaping for values interpolated into LDAP search filters.
// Used by LdapManagementService when it builds the user-facing search filter.
public static class LdapFilterEscaper
{
    public static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value
            .Replace("\\", "\\5c", StringComparison.Ordinal)
            .Replace("*", "\\2a", StringComparison.Ordinal)
            .Replace("(", "\\28", StringComparison.Ordinal)
            .Replace(")", "\\29", StringComparison.Ordinal)
            .Replace("\0", "\\00", StringComparison.Ordinal);
    }
}
