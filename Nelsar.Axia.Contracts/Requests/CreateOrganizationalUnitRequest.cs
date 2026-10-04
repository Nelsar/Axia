namespace Nelsar.Axia.Contracts.Requests;

public class CreateOrganizationalUnitRequest
{
    public string Name { get; set; } = string.Empty;
    public string ParentDistinguishedName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
