using System.Globalization;
using System.Resources;

namespace Nelsar.Axia.Application.Resources;

/// <summary>
/// Typed access to <c>ApplicationMessages.resx</c> — the text of failures the application layer
/// and the directory adapters report to a caller.
///
/// Log messages are deliberately NOT here. Logs are read by operators and consumed by log
/// aggregation, alerting and grep; translating them would break every saved query and make an
/// incident harder to work, for no reader who benefits. Only text that leaves the process in an
/// HTTP response is worth a resource file.
/// </summary>
public static class ApplicationMessages
{
    private static readonly ResourceManager Manager = new(
        "Nelsar.Axia.Application.Resources.ApplicationMessages",
        typeof(ApplicationMessages).Assembly);

    public static string UserAlreadyExistsInDirectory() => Get(nameof(UserAlreadyExistsInDirectory));

    public static string UserAlreadyExists() => Get(nameof(UserAlreadyExists));

    public static string UserNotFoundForUpdate() => Get(nameof(UserNotFoundForUpdate));

    public static string BulkCreateFieldsRequired() => Get(nameof(BulkCreateFieldsRequired));

    public static string BulkOperationUnknown(string operation) => Format(nameof(BulkOperationUnknown), operation);

    /// <summary>Moved here from ApiMessages together with the guard it belongs to: the check is
    /// now a validator on the command, so its text has to be reachable from this layer.</summary>
    public static string BulkRequestEmpty() => Get(nameof(BulkRequestEmpty));

    public static string PatternRequired() => Get(nameof(PatternRequired));

    /// <summary>
    /// The bounds are named in the message on purpose. Page size is something a client sets from
    /// code, and "invalid page size" leaves the author guessing at a number; the limits are not a
    /// secret, and stating them is what turns a rejection into a fix.
    /// </summary>
    public static string PageSizeOutOfRange(int minimum, int maximum) =>
        Format(nameof(PageSizeOutOfRange), minimum, maximum);

    public static string GeneratedItemLimitOutOfRange(int minimum, int maximum) =>
        Format(nameof(GeneratedItemLimitOutOfRange), minimum, maximum);

    public static string DirectoryEnumerationIncomplete() => Get(nameof(DirectoryEnumerationIncomplete));

    public static string DirectoryUnreachable() => Get(nameof(DirectoryUnreachable));

    public static string DirectoryServerNotConfigured() => Get(nameof(DirectoryServerNotConfigured));

    /// <summary>
    /// The TLS handshake failed on the certificate. Deliberately says nothing about why — the
    /// subject, the issuer and the exact failure are in the log, where whoever can fix the trust
    /// configuration is looking.
    /// </summary>
    public static string DirectoryCertificateRejected() => Get(nameof(DirectoryCertificateRejected));

    /// <param name="value">The value given for the security mode.</param>
    public static string LdapSecurityUnknown(string? value) => Format(nameof(LdapSecurityUnknown), value);

    /// <summary>
    /// The caller named a directory that does not exist or has been switched off. It echoes the
    /// id back because the caller supplied it — that tells them which of their headers is wrong,
    /// and reveals nothing they did not already send us. The provider's name stays in the log.
    /// </summary>
    public static string DirectoryProviderUnknown(string providerId) =>
        Format(nameof(DirectoryProviderUnknown), providerId);

    public static string DirectoryProviderUnreachable(string providerId) =>
        Format(nameof(DirectoryProviderUnreachable), providerId);

    /// <summary>
    /// The directory exists and is active, but what is stored about it cannot be used — today
    /// that means a bind credential that will not decrypt. It says to fetch an administrator
    /// rather than what is wrong, because the cause is a server-side matter and the caller's only
    /// move is the same whichever it was. The cause itself goes to the log.
    /// </summary>
    public static string DirectoryProviderMisconfigured(string providerId) =>
        Format(nameof(DirectoryProviderMisconfigured), providerId);

    public static string WriteOperationsUnsupportedOnGlobalCatalog() =>
        Get(nameof(WriteOperationsUnsupportedOnGlobalCatalog));

    // The failures from here to UserNotFoundInAd name no account, and that is deliberate. They
    // used to quote the login or the DN, and an exception's message does not stay with the
    // caller: it is the {Reason} of a refusal in the log, and the text of every logged exception,
    // on its way to Logstash or Loki in full — past LogMask, which only masks what it is handed.
    // The caller loses nothing by it, having just named the account itself; a bulk row's result
    // carries its own address. What the operator needs is the masked subject the providers log
    // next to the failure, and the audit trail, which records the target in full on purpose.

    public static string DirectorySearchFailed() => Get(nameof(DirectorySearchFailed));

    public static string DirectoryCredentialCheckFailed() => Get(nameof(DirectoryCredentialCheckFailed));

    public static string DirectoryGroupMembershipFailed() => Get(nameof(DirectoryGroupMembershipFailed));

    public static string DirectoryRejectedCreate() => Get(nameof(DirectoryRejectedCreate));

    public static string DirectoryRejectedUpdate() => Get(nameof(DirectoryRejectedUpdate));

    public static string DirectoryRejectedDelete() => Get(nameof(DirectoryRejectedDelete));

    public static string UserNotFoundInLdap() => Get(nameof(UserNotFoundInLdap));

    public static string UserNotFoundInAd() => Get(nameof(UserNotFoundInAd));

    public static string OrganizationalUnitNameRequired() => Get(nameof(OrganizationalUnitNameRequired));

    public static string OrganizationalUnitNotFound(string identifier) =>
        Format(nameof(OrganizationalUnitNotFound), identifier);

    public static string OrganizationalUnitLookupIncomplete(string identifier) =>
        Format(nameof(OrganizationalUnitLookupIncomplete), identifier);

    public static string OrganizationalUnitAlreadyExists(string distinguishedName) =>
        Format(nameof(OrganizationalUnitAlreadyExists), distinguishedName);

    public static string OrganizationalUnitNotEmpty(string distinguishedName) =>
        Format(nameof(OrganizationalUnitNotEmpty), distinguishedName);

    public static string OrganizationalUnitsUnsupportedByAdProvider() =>
        Get(nameof(OrganizationalUnitsUnsupportedByAdProvider));

    public static string GroupNameRequired() => Get(nameof(GroupNameRequired));

    public static string GroupMembersRequired() => Get(nameof(GroupMembersRequired));

    public static string GroupNotFound(string identifier) =>
        Format(nameof(GroupNotFound), identifier);

    public static string GroupLookupIncomplete(string identifier) =>
        Format(nameof(GroupLookupIncomplete), identifier);

    public static string GroupAlreadyExists(string distinguishedName) =>
        Format(nameof(GroupAlreadyExists), distinguishedName);

    public static string GroupNotEmptyContainer(string distinguishedName) =>
        Format(nameof(GroupNotEmptyContainer), distinguishedName);

    public static string GroupsUnsupportedByAdProvider() =>
        Get(nameof(GroupsUnsupportedByAdProvider));

    public static string UserAttributesUnsupportedByAdProvider() =>
        Get(nameof(UserAttributesUnsupportedByAdProvider));

    public static string ComputerNameRequired() => Get(nameof(ComputerNameRequired));

    public static string ComputerNotFound(string identifier) =>
        Format(nameof(ComputerNotFound), identifier);

    public static string ComputerLookupIncomplete(string identifier) =>
        Format(nameof(ComputerLookupIncomplete), identifier);

    public static string ComputerAlreadyExists(string distinguishedName) =>
        Format(nameof(ComputerAlreadyExists), distinguishedName);

    public static string ComputerNotEmptyContainer(string distinguishedName) =>
        Format(nameof(ComputerNotEmptyContainer), distinguishedName);

    public static string ComputersUnsupportedByAdProvider() =>
        Get(nameof(ComputersUnsupportedByAdProvider));

    public static string AuthenticationRequired() => Get(nameof(AuthenticationRequired));

    /// <summary>
    /// Says only that the operation was refused. Naming the missing permission would hand a
    /// caller probing the API a map of the authorisation model — that detail goes to the log.
    /// </summary>
    public static string PermissionDenied() => Get(nameof(PermissionDenied));

    /// <summary>
    /// The caller may perform the operation, just not on this account or group, because it can do
    /// something they cannot. Says no more than that, for the reason PermissionDenied does not
    /// name the permission: which ones the target holds is for the log.
    /// </summary>
    public static string TargetHoldsMorePermissions() => Get(nameof(TargetHoldsMorePermissions));

    /// <summary>
    /// The session names a directory entry that is no longer there. A 401, so the console signs
    /// its user out and back in, which finds the account wherever it is now.
    /// </summary>
    public static string SessionEntryGone() => Get(nameof(SessionEntryGone));

    public static string EmailAlreadyInUse() => Get(nameof(EmailAlreadyInUse));

    /// <summary>
    /// Says what to do rather than why: the reason — that the stored password would otherwise be
    /// sent to the new host — is in Provider.WouldReachNewEndpoint, and the person changing the
    /// address needs only to know the password has to come with it.
    /// </summary>
    public static string BindPasswordRequiredForNewEndpoint() => Get(nameof(BindPasswordRequiredForNewEndpoint));

    /// <summary>The same advice for the same reason as the message above; see Provider.WouldDropEncryption.</summary>
    public static string BindPasswordRequiredForPlainLdap() => Get(nameof(BindPasswordRequiredForPlainLdap));

    public static string RoleUnknown(string roleName) => Format(nameof(RoleUnknown), roleName);

    /// <summary>Points at what to do instead — a different mapping — because that is the real
    /// question behind trying to take rights away from Admin.</summary>
    public static string RoleNotEditable(string roleName) => Format(nameof(RoleNotEditable), roleName);

    public static string PermissionNotGrantable(string permission) => Format(nameof(PermissionNotGrantable), permission);

    public static string CannotGrantUnheldPermission() => Get(nameof(CannotGrantUnheldPermission));

    /// <summary>Privileged by the directory's own reckoning; names the usual reason, not the attribute.</summary>
    public static string TargetIsProtected() => Get(nameof(TargetIsProtected));

    public static string ServiceAccountNotManaged() => Get(nameof(ServiceAccountNotManaged));

    /// <summary>The directory's policy said no — the caller's to fix, so it reads as advice.</summary>
    public static string PasswordRejectedByDirectory() => Get(nameof(PasswordRejectedByDirectory));

    public static string PasswordChangeNeedsEncryption() => Get(nameof(PasswordChangeNeedsEncryption));

    public static string PasswordChangeUnsupported() => Get(nameof(PasswordChangeUnsupported));

    private static string Get(string key) =>
        Manager.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    private static string Format(string key, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentUICulture, Get(key), arguments);
}
