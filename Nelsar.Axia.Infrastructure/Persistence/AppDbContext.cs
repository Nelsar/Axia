using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Domain.ValueObjects;
using Nelsar.Axia.Infrastructure.Persistence.Configurations;
using Nelsar.Axia.Infrastructure.Persistence.Converters;

namespace Nelsar.Axia.Infrastructure.Persistence;

public class AppDbContext : DbContext, IAppDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<LdapConfiguration> LdapConfigurations => Set<LdapConfiguration>();
    public DbSet<LdapAttributeMapping> LdapAttributeMappings => Set<LdapAttributeMapping>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<Provider> Providers => Set<Provider>();
    public DbSet<RoleMapping> RoleMappings => Set<RoleMapping>();
    public DbSet<RateLimitSettings> RateLimitSettings => Set<RateLimitSettings>();
    public DbSet<UserLookupEntry> UserLookups => Set<UserLookupEntry>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    /// <summary>
    /// Registered as a convention rather than on the one property, so that any context deriving
    /// from this one maps <see cref="LdapPort"/> without having to know it exists. A derived
    /// context that overrides <see cref="OnModelCreating"/> with its own trimmed-down
    /// configuration — as several tests do — would otherwise fail model building outright.
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<LdapPort>().HaveConversion<LdapPortConverter>();
        configurationBuilder.Properties<EncryptedSecret>().HaveConversion<EncryptedSecretConverter>();
        configurationBuilder.Properties<LdapSearchFilter>().HaveConversion<LdapSearchFilterConverter>();
    }

    /// <summary>
    /// The mapping itself lives one class per entity under <c>Persistence/Configurations</c>,
    /// discovered by scan rather than listed here.
    ///
    /// It used to be one 130-line method: eight entities' worth of column widths, keys and
    /// indexes in a single block that every change to any table had to be merged into, and that
    /// nothing but its own ordering kept apart. Splitting it puts a table's rules in a file named
    /// after the table, and makes adding a ninth entity a new file rather than an edit to the one
    /// class every other entity also goes through.
    ///
    /// A scan rather than eight <c>ApplyConfiguration</c> lines, for the same reason handlers are
    /// scanned in the Application layer: a list of registrations goes stale silently, and a
    /// forgotten entry here would mean a table quietly built by EF's conventions — no lengths, no
    /// indexes, no defaults — rather than a build error.
    /// </summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // What one database needs and the others must not have. The model EF Core builds is
        // cached per provider, so a process that talks to two of them — copy-database does — gets
        // each its own.
        if (Database.IsSqlite())
        {
            modelBuilder.Entity<AuditEvent>()
                .Property(x => x.Timestamp)
                .HasConversion(AuditEventConfiguration.TimestampAsUtcText);
        }

        if (Database.IsNpgsql())
        {
            // PostgreSQL stores an instant, and its driver refuses a DateTimeOffset with any offset
            // but zero rather than convert it. Everything this application writes is UTC already;
            // this keeps a value that is not — a row read from another database, say — from
            // failing a write.
            foreach (var property in modelBuilder.Model.GetEntityTypes()
                         .SelectMany(entity => entity.GetProperties())
                         .Where(property => property.ClrType == typeof(DateTimeOffset)))
            {
                property.SetValueConverter(UtcInstant);
            }
        }
    }

    private static readonly ValueConverter<DateTimeOffset, DateTimeOffset> UtcInstant =
        new(value => value.ToUniversalTime(), value => value);
}
