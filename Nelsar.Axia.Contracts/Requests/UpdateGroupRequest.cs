namespace Nelsar.Axia.Contracts.Requests;

/// <summary>
/// A partial update: every property is optional, and null means "leave this as it is".
///
/// The distinction matters most for the two properties that hold a collection or free text.
/// Null <see cref="MemberDistinguishedNames"/> keeps the current membership, where a
/// non-nullable default of "no members" would empty the group on every rename. Null
/// <see cref="Description"/> keeps the existing text, while an empty string clears it.
/// </summary>
public class UpdateGroupRequest
{
    public string? Name { get; set; }

    /// <summary>A different parent moves the group. Null leaves it where it is.</summary>
    public string? ParentDistinguishedName { get; set; }

    public string? Description { get; set; }

    /// <summary>
    /// Replaces the membership wholesale when given. It may not be replaced with nothing: a
    /// <c>groupOfNames</c> must keep at least one member, so clearing the list is refused rather
    /// than attempted — see <see cref="CreateGroupRequest.MemberDistinguishedNames"/>.
    /// </summary>
    public IEnumerable<string>? MemberDistinguishedNames { get; set; }
}
