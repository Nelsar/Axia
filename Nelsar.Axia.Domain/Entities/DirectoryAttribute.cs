namespace Nelsar.Axia.Domain.Entities;

/// <summary>
/// One attribute of a directory entry, as read through the LDAP port.
///
/// Distinct from the handful of fields on <see cref="LdapUserInfo"/>: those are the attributes
/// the application itself uses, normalised across Active Directory and OpenLDAP naming. This is
/// the raw schema — whatever the entry happens to carry, under the directory's own names.
/// </summary>
/// <param name="Name">The attribute name exactly as the directory reports it.</param>
/// <param name="Values">
/// The values. Empty when <paramref name="IsRedacted"/> — an attribute with no values and an
/// attribute whose values are withheld are different facts, and the flag is what separates them.
/// </param>
/// <param name="IsBinary">
/// The raw bytes are not text, so <paramref name="Values"/> holds base64 instead. Rendering
/// those bytes as a string would produce mojibake that looks like data but decodes to nothing.
/// </param>
/// <param name="IsRedacted">
/// The attribute is on the secret list (password hashes, key material) and its values are never
/// read out of the directory. The name is still reported: that an account carries a credential
/// attribute is ordinary schema information, and hiding the name too would leave an operator
/// unable to tell a redacted attribute from one that is simply absent.
/// </param>
public sealed record DirectoryAttribute(
    string Name,
    IReadOnlyList<string> Values,
    bool IsBinary = false,
    bool IsRedacted = false);
