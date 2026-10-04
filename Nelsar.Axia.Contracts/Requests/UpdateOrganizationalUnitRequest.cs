namespace Nelsar.Axia.Contracts.Requests;

/// <summary>
/// A partial update: every property is optional, and null means "leave this as it is".
///
/// The distinction matters most for <see cref="Description"/> — null keeps the existing text,
/// while an empty string is an explicit request to clear it. With non-nullable defaults, a
/// client sending only a new name would silently erase the description it never mentioned.
/// </summary>
public class UpdateOrganizationalUnitRequest
{
    public string? Name { get; set; }
    public string? ParentDistinguishedName { get; set; }
    public string? Description { get; set; }
}
