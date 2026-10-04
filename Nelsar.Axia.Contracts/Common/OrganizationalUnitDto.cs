namespace Nelsar.Axia.Contracts.Common;

public class OrganizationalUnitDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DistinguishedName { get; set; } = string.Empty;
    public string ParentDistinguishedName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
