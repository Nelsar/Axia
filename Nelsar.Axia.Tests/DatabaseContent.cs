using Microsoft.EntityFrameworkCore;
using Nelsar.Axia.Application.Configuration;
using Nelsar.Axia.Domain.Authorization;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Domain.ValueObjects;
using Nelsar.Axia.Infrastructure.Persistence;

namespace Nelsar.Axia.Tests;

/// <summary>
/// A row in every table the model has, and a way to read every column of every row back, for
/// tests about moving a database. Generic on the read side so that a column added later is
/// compared without anyone adding it here; a table added later fails
/// <see cref="FillEveryTableAsync"/>'s own check until it is given a row.
/// </summary>
internal static class DatabaseContent
{
    /// <summary>Whole milliseconds: PostgreSQL keeps microseconds, SQLite's text keeps ticks.</summary>
    private static readonly DateTimeOffset Start = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    public static async Task FillEveryTableAsync(AppDbContext db, int auditRows = 3)
    {
        var configurationId = Guid.NewGuid();
        db.LdapConfigurations.Add(new LdapConfiguration
        {
            Id = configurationId,
            Host = "ldap.example.org",
            Port = 636,
            UseSsl = true,
            BaseDn = "dc=example,dc=org",
            BindDn = "cn=axia,ou=services,dc=example,dc=org",
            EncryptedBindPassword = "enc:bm9uY2U=:Y2lwaGVy:dGFn",
            UserObjectClass = "inetOrgPerson",
            AttributeMappings =
            [
                new() { Id = Guid.NewGuid(), LdapConfigurationId = configurationId, DomainPropertyName = "Login", LdapAttributeName = "uid" },
            ],
        });

        var provider = Provider.Create(
            "Corp", "dc01.example.com", LdapPort.Create(636), LdapSecurity.Ldaps, "StandardLdap", true,
            "dc02.example.com", "DC=example,DC=com", "svc-axia@example.com",
            EncryptedSecret.FromCipherText("enc:bm9uY2U=:c2VjcmV0:dGFn"), LdapSearchFilter.Default, 250);
        db.Providers.Add(provider);

        db.RoleMappings.Add(RoleMapping.Create(provider.Id, "CN=Axia-Admins,OU=Groups,DC=example,DC=com", UserRoles.Admin, 0));
        db.RoleMappings.Add(RoleMapping.Create(null, "cn=axia-editors,ou=groups,dc=example,dc=org", UserRoles.UserEditor, 10));
        db.RolePermissions.Add(RolePermission.Grant(UserRoles.UserEditor, Permissions.UsersUpdate));
        db.RateLimitSettings.Add(RateLimitSettings.Create("auth-login", 5, 60, 0, true));
        db.UserLookups.Add(UserLookupEntry.Remember(Guid.NewGuid(), "admin.demo@example.org", Start));

        for (var i = 0; i < auditRows; i++)
        {
            db.AuditEvents.Add(i % 2 == 0
                ? AuditEvent.Authentication("admin.demo", true, null, "uid=admin.demo,dc=example,dc=org", null, Start.AddSeconds(i))
                : AuditEvent.Modification(Guid.NewGuid(), "uid=jdoe,dc=example,dc=org", "admin.demo", "UpdateUser", i % 3 != 0,
                    $"row {i}", Start.AddSeconds(i)));
        }

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        foreach (var (table, rows) in await ReadAsync(db))
            Assert.True(rows.Count > 0, $"{table} is new: give it a row here, so that copying it is tested.");
    }

    /// <summary>Every column of every row, by table and by key.</summary>
    public static async Task<Dictionary<string, Dictionary<string, object?[]>>> ReadAsync(AppDbContext db)
    {
        var tables = new Dictionary<string, Dictionary<string, object?[]>>();

        foreach (var table in db.Model.GetEntityTypes())
        {
            var set = (IQueryable<object>)typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!
                .MakeGenericMethod(table.ClrType).Invoke(db, null)!;
            var properties = table.GetProperties().ToList();
            var key = table.FindPrimaryKey()!.Properties.Single();

            var rows = new Dictionary<string, object?[]>();
            foreach (var row in await set.ToListAsync())
            {
                rows[key.GetGetter().GetClrValue(row)!.ToString()!] = properties
                    .Select(property => Comparable(property.GetGetter().GetClrValue(row)))
                    .ToArray();
            }

            tables[table.GetTableName()!] = rows;
        }

        db.ChangeTracker.Clear();
        return tables;
    }

    /// <summary>An instant compares as an instant, whatever offset it was read back with.</summary>
    private static object? Comparable(object? value) =>
        value is DateTimeOffset instant ? instant.UtcTicks : value;

    public static void AssertSame(
        Dictionary<string, Dictionary<string, object?[]>> expected,
        Dictionary<string, Dictionary<string, object?[]>> actual)
    {
        Assert.Equal(expected.Keys.Order(), actual.Keys.Order());

        foreach (var (table, rows) in expected)
        {
            Assert.Equal(rows.Keys.Order(), actual[table].Keys.Order());

            foreach (var (key, values) in rows)
                Assert.True(values.SequenceEqual(actual[table][key]), $"{table} row {key} differs after the copy.");
        }
    }
}
