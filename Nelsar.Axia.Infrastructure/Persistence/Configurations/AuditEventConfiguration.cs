using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Nelsar.Axia.Domain.Entities;

namespace Nelsar.Axia.Infrastructure.Persistence.Configurations;

internal sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    /// <summary>
    /// The text SQLite has always held for an audit timestamp: Microsoft.Data.Sqlite writes a
    /// DateTimeOffset in exactly this form, so rows written before the converter below existed
    /// read back unchanged and sort among the new ones.
    /// </summary>
    internal const string TimestampFormat = @"yyyy\-MM\-dd HH\:mm\:ss.FFFFFFFzzz";

    /// <summary>
    /// Stores the timestamp as that text, always in UTC. On SQLite only — applied by
    /// <see cref="AppDbContext"/>, which knows the provider: PostgreSQL has a type for an instant
    /// and orders by it.
    ///
    /// The point is ordering. EF Core refuses to ORDER BY a DateTimeOffset on SQLite, because
    /// the text it stores compares wrongly across different offsets, and that refusal is why the
    /// audit page used to load the whole table and sort it in memory — on every request, on the
    /// one table that only ever grows. In UTC, every row has the same offset and the text sorts
    /// as the instant does: the fields are fixed-width and most-significant first, and where the
    /// trimmed fraction makes one value shorter, the "+" that ends it sorts below the "." and
    /// every digit the longer one continues with.
    /// </summary>
    internal static readonly ValueConverter<DateTimeOffset, string> TimestampAsUtcText = new(
        value => value.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture),
        text => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture));

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

        // The audit page reads newest first, a page at a time.
        entity.HasIndex(x => x.Timestamp);
    }
}
