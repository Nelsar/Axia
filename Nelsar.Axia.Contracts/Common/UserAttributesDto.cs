namespace Nelsar.Axia.Contracts.Common;

/// <summary>
/// Every attribute one account's directory entry carries, under the directory's own names.
///
/// Separate from <see cref="UserDto"/> and fetched separately: UserDto is the handful of fields
/// the application understands and is returned for every user in a listing, while this is one
/// extra directory read for one entry, made only when someone actually looks at it.
/// </summary>
public class UserAttributesDto
{
    public Guid Id { get; set; }

    /// <summary>The DN of the entry the attributes were read from.</summary>
    public string Dn { get; set; } = string.Empty;

    public IReadOnlyList<DirectoryAttributeDto> Attributes { get; set; } = [];
}

public class DirectoryAttributeDto
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Empty when <see cref="IsRedacted"/>; base64 when <see cref="IsBinary"/>.</summary>
    public IReadOnlyList<string> Values { get; set; } = [];

    /// <summary>Values are base64 of bytes that are not text.</summary>
    public bool IsBinary { get; set; }

    /// <summary>Credential attribute — the name is reported, the values are never read.</summary>
    public bool IsRedacted { get; set; }
}
