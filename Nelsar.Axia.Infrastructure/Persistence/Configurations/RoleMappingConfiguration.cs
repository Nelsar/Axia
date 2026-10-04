using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nelsar.Axia.Domain.Entities;

namespace Nelsar.Axia.Infrastructure.Persistence.Configurations;

internal sealed class RoleMappingConfiguration : IEntityTypeConfiguration<RoleMapping>
{
    public void Configure(EntityTypeBuilder<RoleMapping> entity)
    {
        entity.ToTable("RoleMappings");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.GroupName).IsRequired().HasMaxLength(256);
        entity.Property(x => x.RoleName).IsRequired().HasMaxLength(128);
        entity.Property(x => x.Priority).IsRequired();
        entity.HasIndex(x => new { x.ProviderId, x.GroupName });
    }
}
