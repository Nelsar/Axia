namespace Nelsar.Axia.Contracts.Requests;

public class CreateGroupRequest
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Where the group is created. Empty resolves to the directory's base DN, which is
    /// the only container the adapter can be sure exists.</summary>
    public string ParentDistinguishedName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Member distinguished names. At least one is required: the entry is written as
    /// <c>groupOfNames</c>, whose schema makes <c>member</c> mandatory (RFC 4519 §2.11), so an
    /// empty group is not a thing the directory will store. Rejecting it here says that, where
    /// letting it through would surface as an opaque schema violation from the server.
    /// </summary>
    public IEnumerable<string> MemberDistinguishedNames { get; set; } = Array.Empty<string>();
}
