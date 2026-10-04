using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Nelsar.Axia.Domain.Entities;

namespace Nelsar.Axia.Infrastructure.Persistence;

/// <summary>
/// Contract for <see cref="AppDbContext"/>. Consumers (repositories, seeders, etc.)
/// should depend on this interface instead of the concrete EF Core context so they
/// can be unit tested against a fake/mock without spinning up a real DbContext.
/// </summary>
public interface IAppDbContext
{
    DbSet<LdapConfiguration> LdapConfigurations { get; }
    DbSet<LdapAttributeMapping> LdapAttributeMappings { get; }
    DbSet<AuditEvent> AuditEvents { get; }
    DbSet<Provider> Providers { get; }
    DbSet<RoleMapping> RoleMappings { get; }
    DbSet<RateLimitSettings> RateLimitSettings { get; }
    DbSet<UserLookupEntry> UserLookups { get; }
    DbSet<RolePermission> RolePermissions { get; }

    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
