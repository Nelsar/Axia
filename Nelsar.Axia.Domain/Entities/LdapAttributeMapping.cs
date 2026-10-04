namespace Nelsar.Axia.Domain.Entities;

public class LdapAttributeMapping
{
    public Guid Id { get; set; }
    public Guid LdapConfigurationId { get; set; }
    public string DomainPropertyName { get; set; } = string.Empty;
    public string LdapAttributeName { get; set; } = string.Empty;
}
