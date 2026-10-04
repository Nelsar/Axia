using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nelsar.Axia.Domain.Entities;

namespace Nelsar.Axia.Infrastructure.Persistence.Configurations;

internal sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> entity)
    {
        entity.ToTable("AuditEvents");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.EventType).IsRequired().HasMaxLength(128);
        entity.Property(x => x.Actor).IsRequired().HasMaxLength(256);
        entity.Property(x => x.Action).IsRequired().HasMaxLength(256);
        entity.Property(x => x.Details).HasMaxLength(4000);
        entity.Property(x => x.ObjectDn).HasMaxLength(1024);
        entity.Property(x => x.Timestamp).IsRequired();
    }
}
