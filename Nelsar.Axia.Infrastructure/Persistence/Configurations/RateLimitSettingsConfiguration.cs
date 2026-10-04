using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nelsar.Axia.Domain.Entities;

namespace Nelsar.Axia.Infrastructure.Persistence.Configurations;

internal sealed class RateLimitSettingsConfiguration : IEntityTypeConfiguration<RateLimitSettings>
{
    public void Configure(EntityTypeBuilder<RateLimitSettings> entity)
    {
        entity.ToTable("RateLimitSettings");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.PolicyName).IsRequired().HasMaxLength(128);
        entity.Property(x => x.PermitLimit).IsRequired();
        entity.Property(x => x.WindowSeconds).IsRequired();
        entity.Property(x => x.QueueLimit).IsRequired();
        entity.Property(x => x.Enabled).IsRequired();
        entity.HasIndex(x => x.PolicyName).IsUnique();
    }
}
