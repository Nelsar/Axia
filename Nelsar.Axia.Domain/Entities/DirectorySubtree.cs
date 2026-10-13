namespace Nelsar.Axia.Domain.Entities;

/// <summary>
/// What a container holds that renaming or moving it would carry along: every entry beneath it is
/// re-addressed with it, and some of them the application must not let just anyone re-address.
/// </summary>
/// <param name="HoldsPrivileged">
/// Something beneath it the directory treats as privileged: an entry with adminCount set, a
/// critical system object, or a domain controller's computer account.
/// </param>
/// <param name="HoldsServiceAccount">The account the application binds as is beneath it.</param>
public sealed record DirectorySubtree(bool HoldsPrivileged, bool HoldsServiceAccount);
