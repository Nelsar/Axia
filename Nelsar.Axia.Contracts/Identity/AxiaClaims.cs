namespace Nelsar.Axia.Contracts.Identity;

/// <summary>
/// Claim names this system mints into its own tokens, beyond the registered ones.
///
/// They live in Contracts because a claim name is part of the token's wire format: the service
/// that writes it and the host that reads it are in different assemblies, and a string literal
/// repeated in both is a rename away from a token whose claim nobody looks at any more — which
/// fails open, quietly, as "this session named no directory".
/// </summary>
public static class AxiaClaims
{
    /// <summary>
    /// The directory the holder authenticated against, verbatim as they named it.
    ///
    /// A string rather than a Guid because that is what the header carries: a provider is
    /// usually a Guid naming a row in the providers table, but it may also be the key of an
    /// <c>Ldap:Providers:{id}</c> section in configuration, which is any string at all.
    ///
    /// Absent means the session was opened against the configured default directory. Absent is
    /// therefore meaningful, not missing, and it is not the same as "any directory will do".
    /// </summary>
    public const string LdapProviderId = "ldap_provider_id";

    /// <summary>
    /// The distinguished name of the entry the holder proved their password against — the one
    /// thing that identifies them in the directory unambiguously.
    ///
    /// Permissions used to be resolved by searching the directory for the login in
    /// preferred_username, and taking the first entry that answered. A login is a value of mail,
    /// uid, sAMAccountName or userPrincipalName, none of which the directory has to keep unique —
    /// mail in particular is just an attribute — so two entries could answer, and the session
    /// was then authorised as whichever came back first. Anyone able to set their own mail to an
    /// administrator's could be that entry. A DN names one entry, and it is the entry that was
    /// bound to.
    ///
    /// Required: a token without it is refused at authentication, rather than authorised the old
    /// way.
    /// </summary>
    public const string DistinguishedName = "ldap_dn";
}
