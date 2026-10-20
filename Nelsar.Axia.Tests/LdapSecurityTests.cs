using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Nelsar.Axia.Application;
using Nelsar.Axia.Application.Configuration;
using Nelsar.Axia.Application.Features.Providers.Commands;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Domain.Exceptions;
using Nelsar.Axia.Domain.Repositories;
using Nelsar.Axia.Domain.ValueObjects;
using Nelsar.Axia.Infrastructure.Identity;
using Nelsar.Axia.Infrastructure.Persistence;

namespace Nelsar.Axia.Tests;

/// <summary>
/// How a directory connection is protected, from the setting to the stored row.
///
/// Every connection used to be plain LDAP, so every password — the service account's on each
/// request, a user's on each sign-in — crossed the network as typed. LDAPS is now the default, and
/// plain LDAP has to be asked for by name.
/// </summary>
public class LdapSecurityTests
{
    private const string Filter = "(&(objectClass=person)(uid={0}))";

    // ---- Which mode works on which port -----------------------------------------------------

    [Theory]
    [InlineData(LdapSecurity.Ldaps, 389, true)]       // TLS handshake against a plain listener
    [InlineData(LdapSecurity.Ldaps, 3268, true)]      // the plain global catalog
    [InlineData(LdapSecurity.Ldaps, 636, false)]
    [InlineData(LdapSecurity.Ldaps, 3269, false)]
    [InlineData(LdapSecurity.Ldaps, 10636, false)]    // a port nobody has an opinion about
    [InlineData(LdapSecurity.StartTls, 636, true)]    // plain LDAP against a TLS listener
    [InlineData(LdapSecurity.StartTls, 3269, true)]
    [InlineData(LdapSecurity.StartTls, 389, false)]
    [InlineData(LdapSecurity.None, 636, true)]
    [InlineData(LdapSecurity.None, 389, false)]
    public void AModeContradictsOnlyThePortsItCanNeverWorkOn(LdapSecurity security, int port, bool contradicts)
    {
        Assert.Equal(contradicts, LdapSecurityPorts.Contradicts(security, port));
    }

    [Theory]
    [InlineData("Ldaps", LdapSecurity.Ldaps)]
    [InlineData("ldaps", LdapSecurity.Ldaps)]
    [InlineData("STARTTLS", LdapSecurity.StartTls)]
    [InlineData(" None ", LdapSecurity.None)]
    public void AModeIsReadByName(string value, LdapSecurity expected)
    {
        Assert.True(LdapSecurityPorts.TryParse(value, out var security));
        Assert.Equal(expected, security);
    }

    [Theory]
    [InlineData("2")]      // Enum.TryParse alone would read this as None
    [InlineData("-1")]
    [InlineData("+0")]
    [InlineData("tls")]
    [InlineData("")]
    [InlineData(null)]
    public void ANumberOrAnUnknownName_IsNotAMode(string? value)
    {
        Assert.False(LdapSecurityPorts.TryParse(value, out _));
    }

    [Fact]
    public void AProvider_RefusesLdapsOnThePlainPort_AndSaysWhichPortToUse()
    {
        var error = Assert.Throws<DomainValidationException>(
            () => TestProvider.Create(port: 389, security: LdapSecurity.Ldaps));

        Assert.Contains("636", error.Message);
    }

    [Fact]
    public void AProvider_KeepsItsModeWhenMoved()
    {
        var provider = TestProvider.Create(port: 389, security: LdapSecurity.None);

        provider.PointAt("ldap2.example.com", LdapPort.Create(636), LdapSecurity.Ldaps, null);

        Assert.Equal(LdapSecurity.Ldaps, provider.Security);
        Assert.Equal(636, provider.Port.Value);
    }

    // ---- What the API does with the field ---------------------------------------------------

    [Fact]
    public async Task ADirectoryCreatedWithoutAMode_GetsLdaps()
    {
        var (sender, repo) = Pipeline();

        var created = await sender.Send(new CreateProviderCommand(
            "main", "ldap.example.com", 636, null, "StandardLdap", true, null, "", "", null, Filter, 500));

        Assert.Equal("Ldaps", created.Security);
        repo.Verify(r => r.AddAsync(It.Is<Provider>(p => p.Security == LdapSecurity.Ldaps)));
    }

    [Fact]
    public async Task ADirectoryCreatedWithoutAMode_OnThePlainPort_IsRefused_NotSavedAsPlainLdap()
    {
        // The case an older client produces: port 389, no field. Quietly storing None would make
        // the old insecure behaviour the outcome of saying nothing.
        var (sender, repo) = Pipeline();

        await Assert.ThrowsAsync<DomainValidationException>(() => sender.Send(new CreateProviderCommand(
            "main", "ldap.example.com", 389, null, "StandardLdap", true, null, "", "", null, Filter, 500)));

        repo.Verify(r => r.AddAsync(It.IsAny<Provider>()), Times.Never);
    }

    [Theory]
    [InlineData("tls")]
    [InlineData("2")]
    public async Task AnUnknownMode_IsRefusedByName(string mode)
    {
        var (sender, _) = Pipeline();

        var error = await Assert.ThrowsAsync<DomainValidationException>(() => sender.Send(new CreateProviderCommand(
            "main", "ldap.example.com", 636, mode, "StandardLdap", true, null, "", "", null, Filter, 500)));

        Assert.Contains($"'{mode}'", error.Message);
    }

    [Fact]
    public async Task AnUpdateWithoutAMode_KeepsTheOneTheDirectoryHas()
    {
        // A console from before the field existed sends no mode. Defaulting it to LDAPS on save
        // would switch a plain-LDAP directory to TLS against a server that does not offer it.
        var existing = TestProvider.Create(port: 389, security: LdapSecurity.None);
        var (sender, repo) = Pipeline(existing);

        var updated = await sender.Send(new UpdateProviderCommand(
            existing.Id, "renamed", existing.Host, 389, null, "StandardLdap", true, null, "", "", null, Filter, 500));

        Assert.Equal("None", updated!.Security);
        Assert.Equal(LdapSecurity.None, existing.Security);
    }

    [Fact]
    public async Task AnUpdateNamingAMode_MovesTheDirectoryToIt()
    {
        var existing = TestProvider.Create(port: 389, security: LdapSecurity.None);
        var (sender, _) = Pipeline(existing);

        var updated = await sender.Send(new UpdateProviderCommand(
            existing.Id, "main", existing.Host, 389, "StartTls", "StandardLdap", true, null, "", "", null, Filter, 500));

        Assert.Equal("StartTls", updated!.Security);
    }

    private static (ISender Sender, Mock<IProviderRepository> Repo) Pipeline(Provider? existing = null)
    {
        var repo = new Mock<IProviderRepository>();
        repo.Setup(r => r.AddAsync(It.IsAny<Provider>())).ReturnsAsync((Provider p) => p);
        repo.Setup(r => r.UpdateAsync(It.IsAny<Provider>())).ReturnsAsync((Provider p) => p);
        if (existing is not null)
            repo.Setup(r => r.GetByIdAsync(existing.Id)).ReturnsAsync(existing);

        var encryption = new Mock<IEncryptionService>();
        encryption.Setup(e => e.Encrypt(It.IsAny<string>())).Returns((string s) => "enc:" + s);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplicationLayer();
        services.AddSingleton(repo.Object);
        services.AddSingleton(encryption.Object);
        services.WithAuthorizedCaller();
        return (services.BuildServiceProvider().GetRequiredService<ISender>(), repo);
    }

    // ---- The Ldap section at startup --------------------------------------------------------

    [Fact]
    public void AnUpgradedConfiguration_OnPort389_IsRefused_WithEveryWayOut()
    {
        // What every appsettings.json written before this says: a server, port 389, no mode.
        var settings = new LdapSettings { Server = "ldap.example.com", Port = 389 };

        var problem = LdapSettingsRules.Problem(settings, "Ldap");

        Assert.NotNull(problem);
        Assert.Contains("Ldap:Security is Ldaps", problem);
        Assert.Contains("port 636", problem);
        Assert.Contains("StartTls", problem);
        Assert.Contains("None", problem);
    }

    [Fact]
    public void NoPort_MeansTheStandardOneForTheMode()
    {
        Assert.Null(LdapSettingsRules.Problem(new LdapSettings { Server = "ldap.example.com" }, "Ldap"));
        Assert.Equal(636, new LdapSettings().ResolvedPort);
        Assert.Equal(389, new LdapSettings { Security = LdapSecurity.StartTls }.ResolvedPort);
        Assert.Equal(389, new LdapSettings { Security = LdapSecurity.None }.ResolvedPort);
    }

    [Theory]
    [InlineData(LdapSecurity.StartTls, 636)]
    [InlineData(LdapSecurity.None, 636)]
    public void PlainLdapOnTheTlsPort_IsRefused(LdapSecurity security, int port)
    {
        var problem = LdapSettingsRules.Problem(
            new LdapSettings { Server = "ldap.example.com", Port = port, Security = security }, "Ldap");

        Assert.NotNull(problem);
        Assert.Contains("Ldaps", problem);
    }

    [Fact]
    public void PlainLdap_IsAllowed_WhenItIsAskedForByName()
    {
        Assert.Null(LdapSettingsRules.Problem(
            new LdapSettings { Server = "ldap.example.com", Port = 389, Security = LdapSecurity.None }, "Ldap"));
    }

    [Fact]
    public void WithNoServer_ThePortIsNotChecked()
    {
        // Directories kept in the database only: the Ldap section is never connected with, and an
        // old "Port": 389 left in it should not keep the host from starting.
        Assert.Null(LdapSettingsRules.Problem(new LdapSettings { Port = 389 }, "Ldap"));
    }

    [Fact]
    public void ActiveDirectory_CannotUseStartTls()
    {
        var problem = LdapSettingsRules.Problem(new LdapSettings
        {
            Server = "dc01.example.com",
            ProviderType = "ActiveDirectory",
            Security = LdapSecurity.StartTls
        }, "Ldap");

        Assert.NotNull(problem);
        Assert.Contains("StartTls", problem);
    }

    [Fact]
    public void ATrustedCaThatCannotBeRead_IsRefused_EvenWithNoServer()
    {
        // Database rows use the host's CA, so a broken path matters whether or not this section
        // names a server.
        var path = Path.Combine(Path.GetTempPath(), $"axia-missing-{Guid.NewGuid():N}.pem");

        var problem = LdapSettingsRules.Problem(new LdapSettings { TrustedCaCertificatePath = path }, "Ldap");

        Assert.NotNull(problem);
        Assert.Contains(path, problem);
    }

    [Fact]
    public void TheHost_RefusesToStart_OnLdapsAgainstThePlainPort()
    {
        using var factory = new ApiTestFactory(_ => { }, new Dictionary<string, string?>
        {
            ["Ldap:Server"] = "ldap.example.com",
            ["Ldap:Port"] = "389"
        });

        var failure = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("Ldap:Security is Ldaps", failure.ToString());
    }

    [Fact]
    public void ACopyForAnotherHost_CarriesEverySetting()
    {
        // The copy this replaced named its fields one by one, so a setting added later — this
        // one, for instance — would have been missing from every failover host.
        var original = new LdapSettings();
        foreach (var property in typeof(LdapSettings).GetProperties().Where(p => p.CanWrite))
        {
            object value = property.PropertyType == typeof(string) ? $"value-of-{property.Name}"
                : property.PropertyType == typeof(int) || property.PropertyType == typeof(int?) ? 4242
                : property.PropertyType == typeof(LdapSecurity) ? LdapSecurity.StartTls
                : throw new InvalidOperationException($"Teach this test about {property.PropertyType} ({property.Name}).");
            property.SetValue(original, value);
        }

        var copy = original.ForEndpoint("failover.example.com", 1389);

        Assert.Equal("failover.example.com", copy.Server);
        Assert.Equal(1389, copy.Port);
        foreach (var property in typeof(LdapSettings).GetProperties()
                     .Where(p => p.CanWrite && p.Name is not nameof(LdapSettings.Server) and not nameof(LdapSettings.Port)))
        {
            Assert.Equal(property.GetValue(original), property.GetValue(copy));
        }
    }

    // ---- Directories already in the database ------------------------------------------------

    [Fact]
    public async Task TheMigration_KeepsExistingDirectoriesOnPlainLdap_SoTheUpgradeBreaksNothing()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>().UseAxiaSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync("20261006060417_IndexAuditEventsByTimestamp");
        var id = Guid.NewGuid();
        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText = """
                INSERT INTO Providers (Id, Name, Host, Port, ProviderType, IsActive)
                VALUES ($id, 'head office', 'ldap.example.com', 389, 'StandardLdap', 1)
                """;
            insert.Parameters.AddWithValue("$id", id.ToString().ToUpperInvariant());
            await insert.ExecuteNonQueryAsync();
        }

        await migrator.MigrateAsync();

        var provider = await db.Providers.AsNoTracking().SingleAsync();
        Assert.Equal(LdapSecurity.None, provider.Security);
        Assert.Equal(389, provider.Port.Value);
    }

    [Fact]
    public async Task ANewDirectory_IsStoredWithItsModeByName()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseAxiaSqlite(connection).Options;
        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
            db.Providers.Add(TestProvider.Create(port: 636, security: LdapSecurity.Ldaps));
            await db.SaveChangesAsync();
        }

        await using var read = connection.CreateCommand();
        read.CommandText = "SELECT Security FROM Providers";
        Assert.Equal("Ldaps", await read.ExecuteScalarAsync());
    }
}
