namespace Nelsar.Axia.Domain.Entities;

/// <summary>
/// Every attribute a single directory entry carries, addressed by the DN it was read from.
///
/// The DN travels with the attributes rather than being left to the caller to remember: an
/// account can be reached by more than one address (uid, mail, userPrincipalName all match the
/// portable search filter), so which entry actually answered is not something the caller can
/// infer from what it asked for.
/// </summary>
public sealed record DirectoryEntryAttributes(string Dn, IReadOnlyList<DirectoryAttribute> Attributes);
