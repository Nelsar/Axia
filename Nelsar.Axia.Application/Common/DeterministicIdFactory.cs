using System.Security.Cryptography;
using System.Text;

namespace Nelsar.Axia.Application.Common;

// SHA256(email|dn|fallback) -> Guid. Shared between UserService and
// LdapManagementService so the same LDAP identity always maps to the same
// UserDto.Id regardless of which code path resolved it.
internal static class DeterministicIdFactory
{
    public static Guid BuildUserId(string? email, string? dn = null, string? fallback = null)
    {
        var source = !string.IsNullOrWhiteSpace(email)
            ? email.Trim().ToLowerInvariant()
            : !string.IsNullOrWhiteSpace(dn)
                ? dn.Trim().ToLowerInvariant()
                : (fallback ?? string.Empty).Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(source))
            source = Guid.NewGuid().ToString("N");

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return new Guid(hash.AsSpan(0, 16));
    }

    /// <summary>
    /// SHA256(dn) -> Guid, for directory objects the API exposes by id while LDAP knows them
    /// only by distinguished name. The mapping is one-way by design: the id is reversed by
    /// enumerating the directory and hashing each DN, so nothing has to be stored to keep the
    /// two in step, and an entry keeps its id across restarts and across replicas.
    ///
    /// A renamed or moved entry therefore gets a new id — correctly so. Its DN is its identity
    /// in the directory, and pretending otherwise would mean a local id outliving the thing it
    /// names.
    /// </summary>
    public static Guid BuildDirectoryObjectId(string? dn)
    {
        var source = (dn ?? string.Empty).Trim().ToLowerInvariant();
        if (source.Length == 0)
            return Guid.Empty;

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return new Guid(hash.AsSpan(0, 16));
    }
}
