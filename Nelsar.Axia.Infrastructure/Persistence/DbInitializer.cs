using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nelsar.Axia.Application.Configuration;
using Nelsar.Axia.Domain.Authorization;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Infrastructure.Identity;

namespace Nelsar.Axia.Infrastructure.Persistence;

public static class DbInitializer
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();

        // Before anything is written. The host checks these too when it starts, but that is after
        // this method has run — and seeding is where the encryption key is first used.
        _ = scope.ServiceProvider.GetRequiredService<IOptions<JwtSettings>>().Value;
        _ = scope.ServiceProvider.GetRequiredService<IOptions<EncryptionSettings>>().Value;
        _ = scope.ServiceProvider.GetRequiredService<IOptions<LdapSettings>>().Value;
        var roleMappings = scope.ServiceProvider.GetRequiredService<IOptions<RoleMappingOptions>>().Value;

        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<AppDbContext>>();

        await db.Database.MigrateAsync();
        logger.LogInformation("Database migration applied.");

        await SeedLdapConfigurationAsync(db, logger, scope.ServiceProvider);
        await SeedRoleMappingsAsync(db, logger, roleMappings);
        await SeedRateLimitSettingsAsync(db, logger);
        await SeedRolePermissionsAsync(db, logger);
        await ReportMappingsByNameAsync(db, logger, roleMappings);
        await EnsureAnAdministratorIsMappedAsync(db, roleMappings);
    }

    /// <summary>
    /// Default limits for each policy, editable afterwards via /api/v1/rate-limit-settings
    /// without a redeploy.
    ///
    /// Seeded per policy rather than "only when the table is empty", which is what it used to do.
    /// That shortcut meant a policy added in a later version never reached a database that had
    /// already been seeded once — and a policy with no row of its own falls back to the
    /// restrictive default, so the symptom would have been an endpoint mysteriously throttled at
    /// five requests a minute on every existing deployment and nowhere else.
    /// </summary>
    private static async Task SeedRateLimitSettingsAsync(IAppDbContext db, ILogger logger)
    {
        var defaults = new[]
        {
            // Sign-in attempts per client address, on /api/v1/auth/login and /api/v1/auth/register.
            ("auth-login", 5, 60),

            // The anonymous directory picker on /api/v1/providers/selectable. Looser than
            // sign-in because it is read once per page load and carries no credential to guess,
            // but still bounded: it is unauthenticated, and an unbounded unauthenticated read is
            // a free amplifier whatever it returns.
            ("providers-selectable", 30, 60)
        };

        var existing = await db.RateLimitSettings
            .Select(setting => setting.PolicyName)
            .ToListAsync();

        var missing = defaults
            .Where(policy => !existing.Contains(policy.Item1, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (missing.Count == 0)
        {
            return;
        }

        foreach (var (policyName, permitLimit, windowSeconds) in missing)
        {
            db.RateLimitSettings.Add(Nelsar.Axia.Domain.Entities.RateLimitSettings.Create(
                policyName: policyName,
                permitLimit: permitLimit,
                windowSeconds: windowSeconds,
                queueLimit: 0,
                enabled: true));
        }

        await db.SaveChangesAsync();
        logger.LogInformation("Seeded rate limit settings for {Policies}.",
            string.Join(", ", missing.Select(policy => policy.Item1)));
    }


    /// <summary>
    /// Read-only access, which is all that signing in grants by itself.
    ///
    /// It used to be far more. The first seed reproduced what a bare [Authorize] had allowed, and
    /// that included groups.manage, orgunits.manage, computers.manage and users.bulk for every
    /// account in the directory. Group management alone was enough to become an administrator:
    /// roles come from group membership, so anyone who could edit a group could put themselves in
    /// the one mapped to Admin. users.bulk was worse still — its "update" rows set passwords, so
    /// any employee could reset any account's password, an administrator's included.
    /// </summary>
    private static readonly string[] ReadOnly =
    [
        Permissions.UsersRead,
        Permissions.GroupsRead,
        Permissions.OrganizationalUnitsRead,
        Permissions.ComputersRead,
    ];

    /// <summary>
    /// Who may do what on a fresh database. Anything beyond reading is granted to a role whose
    /// name says it, and changing the directory's structure — groups, units, computer accounts —
    /// is an administrator's alone. Existing databases are brought to the same matrix by the
    /// RevokeAccidentalDefaultGrants migration.
    ///
    /// users.bulk is held only by roles that also hold what a batch's rows do: a row that
    /// creates needs users.create and a row that updates needs users.update, checked per batch
    /// (see ProcessBulkUsersCommand).
    /// </summary>
    internal static IReadOnlyDictionary<string, string[]> DefaultGrants { get; } = new Dictionary<string, string[]>
    {
        [UserRoles.User] = ReadOnly,
        [UserRoles.UserCreator] =
            [.. ReadOnly, Permissions.UsersList, Permissions.UsersCreate, Permissions.UsersBulk, Permissions.UsersGenerate],
        [UserRoles.UserEditor] = [.. ReadOnly, Permissions.UsersUpdate, Permissions.UsersBulk],
        [UserRoles.UserDeleter] = [.. ReadOnly, Permissions.UsersDelete],
        [UserRoles.Admin] = [.. Permissions.All],
    };

    /// <summary>
    /// Writes <see cref="DefaultGrants"/> into an empty table. From then on the table is the
    /// administrator's, and nothing here touches it again.
    /// </summary>
    private static async Task SeedRolePermissionsAsync(IAppDbContext db, ILogger logger)
    {
        if (await db.RolePermissions.AnyAsync())
        {
            return;
        }

        var grants = DefaultGrants;

        foreach (var (role, permissions) in grants)
        {
            foreach (var permission in permissions.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                db.RolePermissions.Add(RolePermission.Grant(role, permission));
            }
        }

        await db.SaveChangesAsync();
        logger.LogInformation("Seeded default role permissions for {RoleCount} roles.", grants.Count);
    }

    /// <summary>
    /// The role mappings a deployment starts with, and the upgrade of the ones it already has.
    ///
    /// On an empty table: one mapping per group in RoleMappings:GroupDns, by DN. Nothing is
    /// guessed: where a directory this instance has never seen keeps its administrators is the
    /// operator's to say. The old by-name defaults are written only where the operator has said
    /// RoleMappings:AllowMappingsByName, which is the old behaviour, hole included.
    ///
    /// On a table that has rows: every global mapping still written by name, for a role
    /// GroupDns now gives a DN for, is converted to that DN. That is the whole upgrade — set the
    /// DNs, restart — and it touches only global rows, because a DN names a group in one
    /// directory and a row scoped to another directory is not about that group.
    /// </summary>
    internal static async Task SeedRoleMappingsAsync(IAppDbContext db, ILogger logger, RoleMappingOptions options)
    {
        if (!await db.RoleMappings.AnyAsync())
        {
            if (options.GroupDns.Count > 0)
            {
                foreach (var (role, groupDn) in options.GroupDns)
                {
                    var canonical = UserRoles.Canonical(role)!;
                    db.RoleMappings.Add(RoleMapping.Create(null, groupDn, canonical, canonical == UserRoles.Admin ? 0 : 10));
                }

                await db.SaveChangesAsync();
                logger.LogInformation("Seeded role mappings for {Roles} from RoleMappings:GroupDns.",
                    string.Join(", ", options.GroupDns.Keys));
            }
            else if (options.AllowMappingsByName)
            {
                db.RoleMappings.AddRange(
                    RoleMapping.SeedByGroupName(null, "Administrators", UserRoles.Admin, 0),
                    RoleMapping.SeedByGroupName(null, "UserCreators", UserRoles.UserCreator, 10),
                    RoleMapping.SeedByGroupName(null, "UserEditors", UserRoles.UserEditor, 10),
                    RoleMapping.SeedByGroupName(null, "UserDeleters", UserRoles.UserDeleter, 10));

                await db.SaveChangesAsync();
                logger.LogWarning("Seeded role mappings by group name, as RoleMappings:AllowMappingsByName asks.");
            }

            return;
        }

        var converted = 0;
        foreach (var mapping in await db.RoleMappings.Where(m => m.ProviderId == null).ToListAsync())
        {
            if (!mapping.IdentifiesGroupByNameOnly
                || UserRoles.Canonical(mapping.RoleName) is not { } role
                || !options.GroupDns.TryGetValue(role, out var groupDn))
            {
                continue;
            }

            logger.LogInformation("Role mapping {MappingId} for {Role}: group '{Group}' becomes {GroupDn}.",
                mapping.Id, role, mapping.GroupName, groupDn);
            mapping.MapGroup(groupDn);
            converted++;
        }

        if (converted > 0)
            await db.SaveChangesAsync();
    }

    /// <summary>
    /// Names every role mapping that still identifies its group by CN alone — as ignored, which is
    /// what they are by default, or as the hole they are where RoleMappings:AllowMappingsByName
    /// keeps them working. Logged on every start, so it cannot be missed for long.
    /// </summary>
    private static async Task ReportMappingsByNameAsync(IAppDbContext db, ILogger logger, RoleMappingOptions options)
    {
        var byName = (await db.RoleMappings.AsNoTracking().ToListAsync())
            .Where(mapping => mapping.IdentifiesGroupByNameOnly)
            .ToList();

        foreach (var mapping in byName)
        {
            if (options.AllowMappingsByName)
            {
                logger.LogWarning(
                    "Role mapping {MappingId} grants {Role} to members of any group named {Group}, wherever it is. "
                    + "Give it the group's distinguished name through /api/v1/role-mappings or RoleMappings:GroupDns.",
                    mapping.Id, mapping.RoleName, mapping.GroupName);
            }
            else
            {
                logger.LogError(
                    "Role mapping {MappingId} names its group as '{Group}' and grants {Role} to nobody: mappings by name "
                    + "are ignored. Set RoleMappings:GroupDns:{Role} to the group's distinguished name and restart.",
                    mapping.Id, mapping.GroupName, mapping.RoleName, mapping.RoleName);
            }
        }
    }

    /// <summary>
    /// Refuses to start when nobody could administer the application: no mapping grants the Admin
    /// role by distinguished name, and mappings by name — which might — are switched off.
    ///
    /// The alternative is an instance that starts and that nobody can sign in to as an
    /// administrator, so nobody can fix the mappings through it either. Better to stop here, with
    /// the setting named, than to leave that to be discovered at the sign-in screen.
    /// </summary>
    internal static async Task EnsureAnAdministratorIsMappedAsync(IAppDbContext db, RoleMappingOptions options)
    {
        if (options.AllowMappingsByName)
            return;

        var mappings = await db.RoleMappings.AsNoTracking().ToListAsync();
        var adminByDn = mappings.Any(mapping =>
            !mapping.IdentifiesGroupByNameOnly
            && string.Equals(mapping.RoleName, UserRoles.Admin, StringComparison.OrdinalIgnoreCase));

        if (!adminByDn)
        {
            throw new InvalidOperationException(
                "No role mapping grants the Admin role by distinguished name, so nobody could administer this "
                + "application. Set RoleMappings:GroupDns:Admin to the DN of the directory group whose members should "
                + "be administrators (for example RoleMappings__GroupDns__Admin=\"CN=Axia-Admins,OU=Groups,DC=example,DC=com\") "
                + "and restart. Mappings by group name are ignored because they match every group of that name.");
        }
    }

    private static async Task SeedLdapConfigurationAsync(IAppDbContext db, ILogger logger, IServiceProvider services)
    {
        if (await db.LdapConfigurations.AnyAsync())
        {
            return;
        }

        var configId = Guid.NewGuid();

        // Seed: OpenLDAP (docker-compose dev environment).
        // EncryptedBindPassword stores the password in plaintext here (no "enc:" prefix),
        // so AesGcmEncryptionService passes it through without decryption.
        // Replace with an "enc:<nonce>:<cipher>:<tag>" value in production.
        var config = new LdapConfiguration
        {
            Id = configId,
            Host = "localhost",
            Port = 389,
            UseSsl = false,
            BaseDn = "dc=example,dc=org",
            BindDn = "cn=admin,dc=example,dc=org",
            EncryptedBindPassword = "adminpassword",
            UserObjectClass = "inetOrgPerson",
            AttributeMappings = new List<LdapAttributeMapping>
            {
                // Domain property  →  LDAP attribute
                // UniqueId: OpenLDAP stores UUID as string; AD sends binary objectGUID
                new() { Id = Guid.NewGuid(), LdapConfigurationId = configId, DomainPropertyName = "UniqueId",    LdapAttributeName = "entryUUID" },
                new() { Id = Guid.NewGuid(), LdapConfigurationId = configId, DomainPropertyName = "Login",       LdapAttributeName = "uid" },
                new() { Id = Guid.NewGuid(), LdapConfigurationId = configId, DomainPropertyName = "Email",       LdapAttributeName = "mail" },
                new() { Id = Guid.NewGuid(), LdapConfigurationId = configId, DomainPropertyName = "Name",        LdapAttributeName = "displayName" },
                // Operational attribute for account creation timestamp (OpenLDAP)
                new() { Id = Guid.NewGuid(), LdapConfigurationId = configId, DomainPropertyName = "CreatedAt",   LdapAttributeName = "createTimestamp" },
                // Group membership — multi-valued
                new() { Id = Guid.NewGuid(), LdapConfigurationId = configId, DomainPropertyName = "MemberOf",    LdapAttributeName = "memberOf" },
                // Used to build the search filter: (&(objectClass=<UserClass>)(<Identity>=<login>))
                new() { Id = Guid.NewGuid(), LdapConfigurationId = configId, DomainPropertyName = "UserClass",   LdapAttributeName = "objectClass" },
                new() { Id = Guid.NewGuid(), LdapConfigurationId = configId, DomainPropertyName = "Identity",    LdapAttributeName = "uid" },
            }
        };

        // Ensure the bind password is stored encrypted when possible.
        try
        {
            var encryptor = services.GetService(typeof(Nelsar.Axia.Application.Interfaces.IEncryptionService)) as dynamic;
            if (encryptor != null)
            {
                var current = config.EncryptedBindPassword ?? string.Empty;
                if (!current.StartsWith("enc:", StringComparison.Ordinal))
                {
                    var encrypted = encryptor.Encrypt(current);
                    config.EncryptedBindPassword = encrypted;
                }
            }
        }
        catch
        {
            // Fail-safe: if encryption service isn't available, leave plaintext (dev-only).
        }

        db.LdapConfigurations.Add(config);
        await db.SaveChangesAsync();
        logger.LogInformation("Seeded default OpenLDAP configuration (id={Id}).", configId);
    }
}
