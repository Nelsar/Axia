namespace Nelsar.Axia.Application.Common;

// Opaque page token backed by a plain integer offset — shared by any service that
// pages a list via Skip/Take (LDAP search results, audit log, ...).
internal static class OffsetPageToken
{
    public static int Parse(string? pageToken)
    {
        if (string.IsNullOrWhiteSpace(pageToken))
            return 0;

        return int.TryParse(pageToken, out var offset) && offset >= 0 ? offset : 0;
    }

    public static string Build(int offset) => offset.ToString();
}
