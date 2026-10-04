using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nelsar.Axia.Domain.Entities;

namespace Nelsar.Axia.Infrastructure.Persistence.Configurations;

internal sealed class LdapAttributeMappingConfiguration : IEntityTypeConfiguration<LdapAttributeMapping>
{
    public void Configure(EntityTypeBuilder<LdapAttributeMapping> entity)
    {
        entity.ToTable("LdapAttributeMappings");
        entity.HasKey(x => x.Id);

        entity.Property(x => x.DomainPropertyName)
            .IsRequired()
            .HasMaxLength(128);

        entity.Property(x => x.LdapAttributeName)
            .IsRequired()
            .HasMaxLength(128);

        entity.HasIndex(x => new { x.LdapConfigurationId, x.DomainPropertyName })
            .IsUnique();
    }
}
