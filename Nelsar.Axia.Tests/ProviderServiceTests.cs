using Nelsar.Axia.Domain.Authorization;
using Nelsar.Axia.Application.Features.RateLimitSettings.Queries;
using Nelsar.Axia.Application.Features.RateLimitSettings.Commands;
using Nelsar.Axia.Application.Features.RoleMappings.Queries;
using Nelsar.Axia.Application.Features.RoleMappings.Commands;
using Nelsar.Axia.Application.Features.Providers.Queries;
using Nelsar.Axia.Application.Features.Providers.Commands;
using Nelsar.Axia.Application;
using Microsoft.Extensions.DependencyInjection;
using MediatR;
using Moq;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Domain.Exceptions;
using Nelsar.Axia.Domain.ValueObjects;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Infrastructure.Identity.Abstractions;
using Nelsar.Axia.Domain.Repositories;
using Nelsar.Axia.Infrastructure.Persistence.Repositories;
using Nelsar.Axia.Application.Services;
using Nelsar.Axia.Contracts.Requests;

namespace Nelsar.Axia.Tests;

public class ProviderServiceTests
{
    /// <summary>The real pipeline over a mocked repository — validators and behaviours included,
    /// so these tests exercise the request exactly as the endpoint issues it.
    ///
    /// That now includes AuthorizationBehaviour, which is why a principal has to be supplied:
    /// these commands declare <c>providers.manage</c>, and without someone holding it the
    /// pipeline refuses before the handler is reached. Refusal itself is covered separately by
    /// <see cref="UseCaseAuthorizationTests"/>.</summary>
    private static ISender CreateSender(IProviderRepository repo, IEncryptionService encryption)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplicationLayer();
        services.AddSingleton(repo);
        services.AddSingleton(encryption);
        services.WithAuthorizedCaller();
        return services.BuildServiceProvider().GetRequiredService<ISender>();
    }

    private static Mock<IEncryptionService> NewEncryptionServiceMock()
    {
        var mock = new Mock<IEncryptionService>();
        mock.Setup(x => x.Encrypt(It.IsAny<string>())).Returns((string s) => $"enc:{s}");
        mock.Setup(x => x.Decrypt(It.IsAny<string>())).Returns((string s) => s.StartsWith("enc:") ? s[4..] : s);
        return mock;
    }

    [Fact]
    public async Task CreateAsync_MapsAndReturnsDto()
    {
        var repo = new Mock<IProviderRepository>();
        // No id to assign here any more: Provider.Create issues one, so the aggregate arrives
        // with an identity rather than acquiring one at the repository.
        repo.Setup(x => x.AddAsync(It.IsAny<Provider>())).ReturnsAsync((Provider p) => p);

        var sender = CreateSender(repo.Object, NewEncryptionServiceMock().Object);

        var result = await sender.Send(new CreateProviderCommand("main", "ldap.example.com", 389, "None", "StandardLdap", true, null, string.Empty, string.Empty, null, "(&(objectClass=person)(|(uid={0})(sAMAccountName={0})(userPrincipalName={0})(mail={0})))", 500));

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("main", result.Name);
        Assert.Equal("ldap.example.com", result.Host);
        Assert.Equal(389, result.Port);
        Assert.Equal("StandardLdap", result.ProviderType);
        Assert.True(result.IsActive);
    }

    [Fact]
    public async Task CreateAsync_EncryptsAdminPassword_AndDoesNotExposeIt()
    {
        var repo = new Mock<IProviderRepository>();
        Provider? saved = null;
        repo.Setup(x => x.AddAsync(It.IsAny<Provider>()))
            .ReturnsAsync((Provider p) =>
            {
                saved = p;
                return p;
            });

        var encryption = NewEncryptionServiceMock();
        var sender = CreateSender(repo.Object, encryption.Object);

        var result = await sender.Send(new CreateProviderCommand("main", "ldap.example.com", 389, "None", "StandardLdap", true, null, string.Empty, "admin@example.com", "SuperSecret1", "(&(objectClass=person)(|(uid={0})(sAMAccountName={0})(userPrincipalName={0})(mail={0})))", 500));

        Assert.NotNull(saved);
        Assert.Equal("enc:SuperSecret1", saved!.EncryptedAdminPassword.Reveal());
        encryption.Verify(x => x.Encrypt("SuperSecret1"), Times.Once);
        Assert.DoesNotContain("SuperSecret1", result.ToString());

        // The ciphertext only comes out through Reveal(). Anything that renders the provider as
        // text — a log line, an interpolated error — gets the mask instead.
        Assert.Equal("***", saved.EncryptedAdminPassword.ToString());
        Assert.DoesNotContain("enc:SuperSecret1", $"{saved.EncryptedAdminPassword}");
    }

    [Fact]
    public async Task UpdateAsync_KeepsExistingPassword_WhenNoneSupplied()
    {
        var existing = TestProvider.Create(encryptedAdminPassword: "enc:OldSecret");
        var providerId = existing.Id;

        var repo = new Mock<IProviderRepository>();
        repo.Setup(x => x.GetByIdAsync(providerId)).ReturnsAsync(existing);
        Provider? saved = null;
        repo.Setup(x => x.UpdateAsync(It.IsAny<Provider>()))
            .ReturnsAsync((Provider p) => { saved = p; return p; });

        var encryption = NewEncryptionServiceMock();
        var sender = CreateSender(repo.Object, encryption.Object);

        await sender.Send(new UpdateProviderCommand(providerId, "main-renamed", "ldap.example.com", 389, null, "StandardLdap", true, null, string.Empty, string.Empty, null, "(&(objectClass=person)(|(uid={0})(sAMAccountName={0})(userPrincipalName={0})(mail={0})))", 500));

        Assert.NotNull(saved);
        Assert.Equal("enc:OldSecret", saved!.EncryptedAdminPassword.Reveal());
        Assert.Equal("main-renamed", saved.Name);
        encryption.Verify(x => x.Encrypt(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsNull_WhenRepositoryReturnsNull()
    {
        var repo = new Mock<IProviderRepository>();
        repo.Setup(x => x.GetByIdAsync(It.IsAny<Guid>()))
            .ReturnsAsync(TestProvider.Create());
        repo.Setup(x => x.UpdateAsync(It.IsAny<Provider>()))
            .ReturnsAsync((Provider?)null);

        var sender = CreateSender(repo.Object, NewEncryptionServiceMock().Object);

        var result = await sender.Send(new UpdateProviderCommand(Guid.NewGuid(), "main", "ldap.example.com", 389, null, "StandardLdap", false, null, string.Empty, string.Empty, null, "(&(objectClass=person)(|(uid={0})(sAMAccountName={0})(userPrincipalName={0})(mail={0})))", 500));

        Assert.Null(result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(70000)]
    public async Task CreateAsync_RejectsAnImpossiblePort_WithoutTouchingTheRepository(int port)
    {
        var repo = new Mock<IProviderRepository>();
        var sender = CreateSender(repo.Object, NewEncryptionServiceMock().Object);

        // [Required] on a non-nullable int never rejected these, so they used to be persisted
        // and then handed to LdapConnection.Connect.
        await Assert.ThrowsAsync<DomainValidationException>(() => sender.Send(new CreateProviderCommand("main", "ldap.example.com", port, "None", "StandardLdap", true, null, string.Empty, string.Empty, null, "(&(objectClass=person)(|(uid={0})(sAMAccountName={0})(userPrincipalName={0})(mail={0})))", 500)));

        repo.Verify(x => x.AddAsync(It.IsAny<Provider>()), Times.Never);
    }
}
