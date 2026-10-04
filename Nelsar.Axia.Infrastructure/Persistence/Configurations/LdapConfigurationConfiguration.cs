using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nelsar.Axia.Domain.Entities;

namespace Nelsar.Axia.Infrastructure.Persistence.Configurations;

internal sealed class LdapConfigurationConfiguration : IEntityTypeConfiguration<LdapConfiguration>
{
    public void Configure(EntityTypeBuilder<LdapConfiguration> entity)
    {
        entity.ToTable("LdapConfigurations");
        entity.HasKey(x => x.Id);

        entity.Property(x => x.Host)
            .IsRequired()
            .HasMaxLength(256);

        entity.Property(x => x.BaseDn)
            .IsRequired()
            .HasMaxLength(512);

        entity.Property(x => x.BindDn)
            .IsRequired()
            .HasMaxLength(512);

        entity.Property(x => x.EncryptedBindPassword)
            .IsRequired()
            .HasMaxLength(2048);

        entity.Property(x => x.UserObjectClass)
            .IsRequired()
            .HasMaxLength(128);

        entity.HasMany(x => x.AttributeMappings)
            .WithOne()
            .HasForeignKey(x => x.LdapConfigurationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
