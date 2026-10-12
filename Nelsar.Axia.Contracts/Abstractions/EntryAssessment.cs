namespace Nelsar.Axia.Contracts.Abstractions;

/// <summary>
/// What it would take to change a directory entry — asked about a target before anyone acts on it.
/// </summary>
/// <param name="Permissions">What the entry would hold if it signed in.</param>
/// <param name="IsProtected">
/// The directory treats it as privileged — Active Directory's adminCount, or membership of a group
/// listed in DirectoryProtection:ProtectedGroups. Its rights are the directory's, not this
/// application's, so <paramref name="Permissions"/> says nothing about them.
/// </param>
/// <param name="IsServiceAccount">The account this application binds to the directory as.</param>
/// <param name="IsDisabled">
/// The directory has switched the account off. It holds no session; as a target it is assessed like
/// any other, since an administrator still has to be able to delete it or switch it back on.
/// </param>
public sealed record EntryAssessment(
    IReadOnlySet<string> Permissions,
    bool IsProtected,
    bool IsServiceAccount,
    bool IsDisabled = false);
