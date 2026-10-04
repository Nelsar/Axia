namespace Nelsar.Axia.Contracts.Requests;

/// <summary>
/// Creates a computer account entry.
///
/// What this does and does not do is worth stating plainly: it writes a directory object, and
/// nothing more. An Active Directory computer account created over LDAP has no machine password
/// and no trust relationship — the machine still has to join the domain itself, at which point
/// it takes over this entry. Creating one here is how an account is pre-staged into the right
/// organizational unit before the join; it does not put a computer on the domain.
/// </summary>
public class CreateComputerRequest
{
    /// <summary>
    /// The machine name, without a trailing <c>$</c>. The account name the directory needs
    /// (<c>sAMAccountName</c>) is derived from it, since that form is an implementation detail
    /// of Active Directory rather than something a caller should have to know.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Where the account is created. Empty resolves to the directory's base DN, which
    /// is the only container the adapter can be sure exists.</summary>
    public string ParentDistinguishedName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>The machine's fully qualified DNS name. Optional — a pre-staged account
    /// usually has none until the machine joins and sets it.</summary>
    public string DnsHostName { get; set; } = string.Empty;
}
