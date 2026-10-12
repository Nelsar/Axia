using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Nelsar.Axia.Application;
using Nelsar.Axia.Application.Features.Providers.Commands;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Domain.Exceptions;
using Nelsar.Axia.Domain.Repositories;
using Nelsar.Axia.Domain.ValueObjects;

namespace Nelsar.Axia.Tests;

/// <summary>
/// A provider's stored bind password goes wherever the provider connects. Moving the provider to
/// a new host while keeping the password used to be one request for anyone holding
/// providers.manage — after which this server sent the service account's password, in a simple
/// bind, to the host they named. That is the LDAP pass-back attack; moving a provider somewhere
/// new now takes the password with it.
/// </summary>
public class LdapPassBackTests
{
    private const string Filter = "(&(objectClass=person)(|(uid={0})(sAMAccountName={0})(userPrincipalName={0})(mail={0})))";

    [Theory]
    [InlineData("dc01.corp.local", 389, "dc02.corp.local", false)]            // nothing changes
    [InlineData("DC01.CORP.LOCAL", 389, " dc02.corp.local ", false)]          // spelling only
    [InlineData("dc02.corp.local", 389, "dc01.corp.local", false)]            // primary and failover swapped
    [InlineData("dc01.corp.local", 389, null, false)]                         // a failover host dropped
    [InlineData("attacker.example", 389, "dc02.corp.local", true)]            // a new primary
    [InlineData("dc01.corp.local", 389, "dc02.corp.local,attacker.example", true)] // a new failover host
    [InlineData("dc01.corp.local", 3890, "dc02.corp.local", true)]            // a port nothing listened on before
    public void WouldReachNewEndpoint_IsAboutWhereTheProviderWillConnect(
        string host, int port, string? failoverHosts, bool expected)
    {
        var provider = TestProvider.Create(host: "dc01.corp.local", port: 389, failoverHosts: "dc02.corp.local");

        Assert.Equal(expected, provider.WouldReachNewEndpoint(host, LdapPort.Create(port), failoverHosts));
    }

    private static (ISender Sender, Mock<IProviderRepository> Repo) Pipeline(Provider existing)
    {
        var repo = new Mock<IProviderRepository>();
        repo.Setup(x => x.GetByIdAsync(existing.Id)).ReturnsAsync(existing);
        repo.Setup(x => x.UpdateAsync(It.IsAny<Provider>())).ReturnsAsync((Provider p) => p);

        var encryption = new Mock<IEncryptionService>();
        encryption.Setup(x => x.Encrypt(It.IsAny<string>())).Returns((string s) => $"enc:{s}");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplicationLayer();
        services.AddSingleton(repo.Object);
        services.AddSingleton(encryption.Object);
        services.WithAuthorizedCaller();

        return (services.BuildServiceProvider().GetRequiredService<ISender>(), repo);
    }

    private static UpdateProviderCommand MoveTo(Provider provider, string host, string? password, string? failoverHosts = null) =>
        // No security mode: an update that omits it keeps the provider's own.
        new(provider.Id, provider.Name, host, provider.Port.Value, null, provider.ProviderType, true,
            failoverHosts, provider.BaseDn, "cn=svc,dc=corp,dc=local", password, Filter, 500);

    private static UpdateProviderCommand Secure(Provider provider, string security, string? password) =>
        new(provider.Id, provider.Name, provider.Host, provider.Port.Value, security, provider.ProviderType, true,
            provider.FailoverHosts, provider.BaseDn, "cn=svc,dc=corp,dc=local", password, Filter, 500);

    [Theory]
    [InlineData(LdapSecurity.Ldaps, LdapSecurity.None, true)]
    [InlineData(LdapSecurity.StartTls, LdapSecurity.None, true)]
    [InlineData(LdapSecurity.None, LdapSecurity.None, false)]      // already plain: nothing new is exposed
    [InlineData(LdapSecurity.None, LdapSecurity.Ldaps, false)]     // turning encryption on
    [InlineData(LdapSecurity.Ldaps, LdapSecurity.StartTls, false)] // one encrypted mode for the other
    [InlineData(LdapSecurity.StartTls, LdapSecurity.Ldaps, false)]
    public void WouldDropEncryption_IsAboutWhetherThePasswordWouldTravelInClearText(
        LdapSecurity from, LdapSecurity to, bool expected)
    {
        // A port no mode contradicts, so that every pair here is one the provider can be moved between.
        var provider = TestProvider.Create(port: 10636, security: from);

        Assert.Equal(expected, provider.WouldDropEncryption(to));
    }

    [Fact]
    public async Task TurningEncryptionOff_WithoutThePassword_IsRefused_AndNothingIsSaved()
    {
        // Same host, same port, so nothing for WouldReachNewEndpoint to see — and the stored
        // password would still have gone out on the next bind, readable by anyone on the path.
        var provider = TestProvider.Create(host: "dc01.corp.local", port: 389, security: LdapSecurity.StartTls,
            encryptedAdminPassword: "enc:ServiceAccountSecret");
        var (sender, repo) = Pipeline(provider);

        await Assert.ThrowsAsync<DomainValidationException>(
            () => sender.Send(Secure(provider, "None", password: null)));

        repo.Verify(x => x.UpdateAsync(It.IsAny<Provider>()), Times.Never);
        Assert.Equal(LdapSecurity.StartTls, provider.Security);
    }

    [Fact]
    public async Task TurningEncryptionOff_WithThePassword_GoesThrough()
    {
        var provider = TestProvider.Create(host: "dc01.corp.local", port: 389, security: LdapSecurity.StartTls,
            encryptedAdminPassword: "enc:Old");
        var (sender, _) = Pipeline(provider);

        await sender.Send(Secure(provider, "None", password: "New"));

        Assert.Equal(LdapSecurity.None, provider.Security);
    }

    [Fact]
    public async Task TurningEncryptionOn_NeedsNoPassword()
    {
        var provider = TestProvider.Create(host: "dc01.corp.local", port: 389, security: LdapSecurity.None,
            encryptedAdminPassword: "enc:ServiceAccountSecret");
        var (sender, _) = Pipeline(provider);

        await sender.Send(Secure(provider, "StartTls", password: null));

        Assert.Equal(LdapSecurity.StartTls, provider.Security);
    }

    [Fact]
    public async Task MovingAProviderToANewHost_WithoutItsPassword_IsRefused_AndNothingIsSaved()
    {
        var provider = TestProvider.Create(host: "dc01.corp.local", encryptedAdminPassword: "enc:ServiceAccountSecret");
        var (sender, repo) = Pipeline(provider);

        await Assert.ThrowsAsync<DomainValidationException>(
            () => sender.Send(MoveTo(provider, "attacker.example", password: null)));

        repo.Verify(x => x.UpdateAsync(It.IsAny<Provider>()), Times.Never);
        Assert.Equal("dc01.corp.local", provider.Host);
    }

    [Fact]
    public async Task AddingAFailoverHost_WithoutThePassword_IsRefusedToo()
    {
        var provider = TestProvider.Create(host: "dc01.corp.local", encryptedAdminPassword: "enc:ServiceAccountSecret");
        var (sender, repo) = Pipeline(provider);

        await Assert.ThrowsAsync<DomainValidationException>(
            () => sender.Send(MoveTo(provider, "dc01.corp.local", password: null, failoverHosts: "attacker.example")));

        repo.Verify(x => x.UpdateAsync(It.IsAny<Provider>()), Times.Never);
    }

    [Fact]
    public async Task MovingAProvider_WithItsPassword_GoesThrough()
    {
        // Whoever supplies the password already knows it; sending it to the new host gives
        // nothing away.
        var provider = TestProvider.Create(host: "dc01.corp.local", encryptedAdminPassword: "enc:Old");
        var (sender, _) = Pipeline(provider);

        var updated = await sender.Send(MoveTo(provider, "dc03.corp.local", password: "New"));

        Assert.NotNull(updated);
        Assert.Equal("dc03.corp.local", provider.Host);
        Assert.Equal("enc:New", provider.EncryptedAdminPassword.Reveal());
    }

    [Fact]
    public async Task MovingAProviderWithNoStoredPassword_GoesThrough()
    {
        // An anonymous bind: there is no password to send anywhere.
        var provider = TestProvider.Create(host: "dc01.corp.local");
        var (sender, _) = Pipeline(provider);

        await sender.Send(MoveTo(provider, "dc03.corp.local", password: null));

        Assert.Equal("dc03.corp.local", provider.Host);
    }
}
