using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nelsar.Axia.Domain.Entities;

namespace Nelsar.Axia.Infrastructure.Persistence.Configurations;

internal sealed class UserLookupEntryConfiguration : IEntityTypeConfiguration<UserLookupEntry>
{
    public void Configure(EntityTypeBuilder<UserLookupEntry> entity)
    {
        entity.ToTable("UserLookups");
        entity.HasKey(x => x.Id);
        // 320 = RFC 3696 maximum (64-char local part + '@' + 255-char domain).
        entity.Property(x => x.Email).IsRequired().HasMaxLength(320);
        entity.Property(x => x.LastSeenAt).IsRequired();
        entity.HasIndex(x => x.Email);
    }
}
