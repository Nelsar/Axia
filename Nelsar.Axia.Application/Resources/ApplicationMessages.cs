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

    public static string DirectorySearchFailed(string login) => Format(nameof(DirectorySearchFailed), login);

    public static string DirectoryCredentialCheckFailed(string login) =>
        Format(nameof(DirectoryCredentialCheckFailed), login);

    public static string DirectoryGroupMembershipFailed(string login) =>
        Format(nameof(DirectoryGroupMembershipFailed), login);

    public static string DirectoryEntryMissingForMembership(string login) =>
        Format(nameof(DirectoryEntryMissingForMembership), login);

    public static string AdPrincipalMissingForMembership(string login) =>
        Format(nameof(AdPrincipalMissingForMembership), login);

    public static string DirectoryRejectedCreate(string distinguishedName) =>
        Format(nameof(DirectoryRejectedCreate), distinguishedName);

    public static string DirectoryRejectedUpdate(string distinguishedName) =>
        Format(nameof(DirectoryRejectedUpdate), distinguishedName);

    public static string DirectoryRejectedDelete(string login) =>
        Format(nameof(DirectoryRejectedDelete), login);

    public static string UserNotFoundInLdap(string login) => Format(nameof(UserNotFoundInLdap), login);

    public static string UserNotFoundInAd(string login) => Format(nameof(UserNotFoundInAd), login);

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

    private static string Get(string key) =>
        Manager.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    private static string Format(string key, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentUICulture, Get(key), arguments);
}
