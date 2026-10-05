namespace Nelsar.Axia.Application.Common.Auditing;

/// <summary>
/// Marks a use case that changes something, so that <c>AuditBehaviour</c> records every attempt
/// at it — who, what, against which directory, and whether it went through.
///
/// The trail used to be written by subscribers to four domain events: sign-in succeeded, sign-in
/// failed, user created, user updated. Everything else that changes a directory or this
/// application's own configuration — deleting accounts, bulk batches and the passwords they
/// reset, groups, units, computers, connection profiles, role mappings, rate limits — left no
/// trace, and the two user events recorded the account that was changed in the actor column
/// rather than the person who changed it. A test now requires every command to carry this
/// marker, so a new one cannot be added without saying how it appears in the trail.
/// </summary>
internal interface IAuditedCommand
{
    /// <summary>
    /// What the command acts on, as someone reading the trail would name it: an id, an address,
    /// a name. Never a secret — this is written verbatim.
    /// </summary>
    string AuditTarget { get; }

    /// <summary>
    /// What the change was, where the target alone does not say: a group's new membership, a
    /// password reset, a provider's new host. Null when there is nothing to add. The same rule
    /// as for the target applies — never a secret, only the fact that one changed.
    /// </summary>
    string? AuditDescription => null;
}

/// <summary>Formatting shared by everything that writes to the trail.</summary>
internal static class AuditDetails
{
    public static string Directory(Guid? providerId) =>
        $"directory={(providerId is { } id ? id.ToString() : "default")}";
}
