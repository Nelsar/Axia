namespace Nelsar.Axia.Domain.Entities;

public class LdapConfiguration
{
    public Guid Id { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 389;
    public bool UseSsl { get; set; }
    public string BaseDn { get; set; } = string.Empty;
    public string BindDn { get; set; } = string.Empty;
    public string EncryptedBindPassword { get; set; } = string.Empty;
    public string UserObjectClass { get; set; } = "person";
    public ICollection<LdapAttributeMapping> AttributeMappings { get; set; } = new List<LdapAttributeMapping>();
}
