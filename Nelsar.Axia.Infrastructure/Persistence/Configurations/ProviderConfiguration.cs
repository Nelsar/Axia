using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Domain.ValueObjects;

namespace Nelsar.Axia.Infrastructure.Persistence.Configurations;

internal sealed class ProviderConfiguration : IEntityTypeConfiguration<Provider>
{
    public void Configure(EntityTypeBuilder<Provider> entity)
    {
        entity.ToTable("Providers");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Name).IsRequired().HasMaxLength(256);
        entity.Property(x => x.Host).IsRequired().HasMaxLength(256);
        // Conversion comes from AppDbContext.ConfigureConventions; the column stays INTEGER.
        entity.Property(x => x.Port).IsRequired();
        entity.Property(x => x.ProviderType).IsRequired().HasMaxLength(128);
        entity.Property(x => x.IsActive).IsRequired();
        entity.Property(x => x.FailoverHosts).HasMaxLength(1024);
        entity.Property(x => x.BaseDn).IsRequired().HasMaxLength(512).HasDefaultValue(string.Empty);
        entity.Property(x => x.AdminUsername).IsRequired().HasMaxLength(256).HasDefaultValue(string.Empty);
        // Defaults are given as the value objects themselves so the converters decide what lands
        // in the column — the stored text is unchanged either way.
        entity.Property(x => x.EncryptedAdminPassword).IsRequired().HasMaxLength(2048)
            .HasDefaultValue(EncryptedSecret.None);
        entity.Property(x => x.UserSearchFilter).IsRequired().HasMaxLength(LdapSearchFilter.MaximumLength)
            .HasDefaultValue(LdapSearchFilter.Default);
        entity.Property(x => x.UserSearchPageSize).IsRequired().HasDefaultValue(500);
    }
}
