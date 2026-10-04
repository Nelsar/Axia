using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nelsar.Axia.Domain.Entities;

namespace Nelsar.Axia.Infrastructure.Persistence.Configurations;

internal sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> entity)
    {
        entity.ToTable("RolePermissions");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.RoleName).IsRequired().HasMaxLength(128);
        entity.Property(x => x.Permission).IsRequired().HasMaxLength(128);
        // One grant per (role, permission): re-granting must not silently duplicate rows, which
        // would make a later revoke look like it only half worked.
        entity.HasIndex(x => new { x.RoleName, x.Permission }).IsUnique();
    }
}
