using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Security.Cryptography;
using Nelsar.Axia.Application.Resources;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Domain.Exceptions;
using Nelsar.Axia.Domain.ValueObjects;
using Nelsar.Axia.Infrastructure.Identity;
using Nelsar.Axia.Contracts.Exceptions;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Infrastructure.Identity.Abstractions;
using Nelsar.Axia.Domain.Repositories;
using Nelsar.Axia.Infrastructure.Persistence.Repositories;

namespace Nelsar.Axia.Tests;

public class DefaultLdapProviderFactoryTests
{
    private static DefaultLdapProviderFactory BuildFactory(
        Mock<IProviderRepository> repoMock,
        Mock<ILdapProvider> defaultProviderMock,
        Mock<IEncryptionService>? encryptionMock = null,
        IConfiguration? configuration = null,
        LdapProviderScope? providerScope = null)
    {
        var sp = new Mock<IServiceProvider>();
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        var config = configuration ?? new ConfigurationBuilder().Build();
        var encryption = encryptionMock ?? new Mock<IEncryptionService>();

        return new DefaultLdapProviderFactory(
            sp.Object,
            config,
            NullLogger<DefaultLdapProviderFactory>.Instance,
            defaultProviderMock.Object,
            httpContextAccessor.Object,
            repoMock.Object,
            encryption.Object,
            new MemoryCache(new MemoryCacheOptions()),
            // Unset unless a test says otherwise, which is the state of every HTTP request:
            // the header decides there, not this.
            providerScope ?? new LdapProviderScope());
    }

    [Fact]
    public void Resolve_RefusesWith502_WhenTheNamedDomainIsUnreachable()
    {
        // The case this whole class is about. A caller aimed at this domain; every host of it is
        // down. Answering with the default provider would carry the operation out against a
        // different directory — and for a DELETE, that is a deletion in the wrong domain,
        // answered 200. It has to fail instead, and as a 502, because nothing about the request
        // was wrong.
        var provider = TestProvider.Create(
            name: "unreachable",
            host: "unreachable.invalid.nelsar-axia.test",
            port: 65000,
            baseDn: "dc=example,dc=com",
            adminUsername: "admin",
            encryptedAdminPassword: "enc:secret");
        var providerId = provider.Id;

        var repo = new Mock<IProviderRepository>();
        repo.Setup(x => x.GetByIdAsync(providerId)).ReturnsAsync(provider);

        var defaultProvider = new Mock<ILdapProvider>();
        var encryption = new Mock<IEncryptionService>();
        encryption.Setup(x => x.Decrypt("enc:secret")).Returns("secret");

        var factory = BuildFactory(repo, defaultProvider, encryption);

        var error = Assert.Throws<DirectoryUnavailableException>(() => factory.Resolve(providerId.ToString()));

        Assert.Contains(providerId.ToString(), error.Message);
        repo.Verify(x => x.GetByIdAsync(providerId), Times.Once);
    }

    [Fact]
    public void Resolve_WithoutARequest_UsesTheDomainTheScopeNames()
    {
        // Work dispatched off the request thread — a queued bulk batch — has no HttpContext to
        // read a header from. It used to get the default provider for that reason alone, which
        // meant a batch queued against this domain was carried out against a different one.
        //
        // The domain named here is unreachable, so a factory that honours the scope fails with
        // 502 and a factory that ignores it quietly hands back the default. Those two outcomes
        // are what tell the versions apart.
        var provider = TestProvider.Create(
            name: "branch",
            host: "unreachable.invalid.nelsar-axia.test",
            port: 65000,
            baseDn: "dc=branch,dc=example,dc=com",
            adminUsername: "admin",
            encryptedAdminPassword: "enc:secret");

        var repo = new Mock<IProviderRepository>();
        repo.Setup(x => x.GetByIdAsync(provider.Id)).ReturnsAsync(provider);

        var defaultProvider = new Mock<ILdapProvider>();
        var encryption = new Mock<IEncryptionService>();
        encryption.Setup(x => x.Decrypt("enc:secret")).Returns("secret");

        var scope = new LdapProviderScope();
        scope.Use(provider.Id);

        var factory = BuildFactory(repo, defaultProvider, encryption, providerScope: scope);

        Assert.Throws<DirectoryUnavailableException>(() => factory.Resolve());
        repo.Verify(x => x.GetByIdAsync(provider.Id), Times.Once);
    }

    [Fact]
    public void Resolve_WithoutARequest_FallsBackToTheDefault_WhenTheScopeNamesNoDomain()
    {
        // Unchanged behaviour, pinned: work queued by a caller who named no domain still runs
        // where it always did, and nothing is asked of the provider table to establish that.
        var repo = new Mock<IProviderRepository>();
        var defaultProvider = new Mock<ILdapProvider>();

        var factory = BuildFactory(repo, defaultProvider);

        Assert.Same(defaultProvider.Object, factory.Resolve());
        repo.Verify(x => x.GetByIdAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public void Resolve_RefusesWith400_WhenTheNamedDomainIsDeactivated()
    {
        // Deactivating a provider is how an administrator takes a domain out of service. Serving
        // its requests from the default domain instead would make that switch do the opposite of
        // what it says.
        var provider = TestProvider.Create(name: "disabled", isActive: false);
        var providerId = provider.Id;

        var repo = new Mock<IProviderRepository>();
        repo.Setup(x => x.GetByIdAsync(providerId)).ReturnsAsync(provider);

        var defaultProvider = new Mock<ILdapProvider>();
        var factory = BuildFactory(repo, defaultProvider);

        Assert.Throws<DomainValidationException>(() => factory.Resolve(providerId.ToString()));
    }

    [Fact]
    public void Resolve_RefusesWith400_WhenNoProviderAnswersToTheId()
    {
        var providerId = Guid.NewGuid();
        var repo = new Mock<IProviderRepository>();
        repo.Setup(x => x.GetByIdAsync(providerId)).ReturnsAsync((Provider?)null);

        var defaultProvider = new Mock<ILdapProvider>();
        var factory = BuildFactory(repo, defaultProvider);

        var error = Assert.Throws<DomainValidationException>(() => factory.Resolve(providerId.ToString()));

        // The id came from the caller's own header, so echoing it tells them which one to fix.
        Assert.Contains(providerId.ToString(), error.Message);
    }

    [Fact]
    public void Resolve_RefusesWith502_WhenTheStoredBindCredentialCannotBeRead()
    {
        // Rotating Encryption:Key without re-encrypting the stored passwords does this to every
        // provider at once; a hand-edited row does it to one. It used to escape as an unhandled
        // FormatException — a 500 logged as "unhandled exception processing POST /api/auth/login",
        // naming no provider, leaving an operator to read a stack trace to find out which of
        // their directories was broken.
        var provider = TestProvider.Create(name: "branch", encryptedAdminPassword: "enc:not-really");
        var providerId = provider.Id;

        var repo = new Mock<IProviderRepository>();
        repo.Setup(x => x.GetByIdAsync(providerId)).ReturnsAsync(provider);

        var encryption = new Mock<IEncryptionService>();
        encryption.Setup(x => x.Decrypt(It.IsAny<string>()))
            .Throws(new FormatException("Encrypted payload format must be enc:<nonceB64>:<cipherB64>:<tagB64>."));

        var factory = BuildFactory(repo, new Mock<ILdapProvider>(), encryption);

        var error = Assert.Throws<DirectoryUnavailableException>(() => factory.Resolve(providerId.ToString()));

        Assert.Equal(ApplicationMessages.DirectoryProviderMisconfigured(providerId.ToString()), error.Message);
    }

    [Theory]
    [InlineData(typeof(CryptographicException))]   // wrong key, or a tampered row
    [InlineData(typeof(FormatException))]          // the payload is not enc:<nonce>:<cipher>:<tag>
    [InlineData(typeof(ArgumentException))]        // nonce or tag of the wrong length
    [InlineData(typeof(InvalidOperationException))] // Encryption:Key is not configured at all
    public void EveryWayDecryptionCanFail_IsReportedAsAnUnusableDirectory(Type failure)
    {
        // Narrow catches are right here, but a missed type silently returns this to a 500 — so
        // each one the crypto path can raise is pinned rather than assumed.
        var provider = TestProvider.Create(encryptedAdminPassword: "enc:whatever");
        var providerId = provider.Id;

        var repo = new Mock<IProviderRepository>();
        repo.Setup(x => x.GetByIdAsync(providerId)).ReturnsAsync(provider);

        var encryption = new Mock<IEncryptionService>();
        encryption.Setup(x => x.Decrypt(It.IsAny<string>()))
            .Throws((Exception)Activator.CreateInstance(failure)!);

        var factory = BuildFactory(repo, new Mock<ILdapProvider>(), encryption);

        Assert.Throws<DirectoryUnavailableException>(() => factory.Resolve(providerId.ToString()));
    }

    [Fact]
    public void AnAnonymousBind_IsNotMistakenForABrokenCredential()
    {
        // A blank stored credential is a real configuration for a read-only directory, not a
        // failure to read one. It has to reach the host probe and fail there instead — which is
        // a different message and a different thing to go and fix.
        var provider = TestProvider.Create(
            host: "unreachable.invalid.nelsar-axia.test",
            port: 65000,
            encryptedAdminPassword: "");
        var providerId = provider.Id;

        var repo = new Mock<IProviderRepository>();
        repo.Setup(x => x.GetByIdAsync(providerId)).ReturnsAsync(provider);

        var encryption = new Mock<IEncryptionService>();
        var factory = BuildFactory(repo, new Mock<ILdapProvider>(), encryption);

        var error = Assert.Throws<DirectoryUnavailableException>(() => factory.Resolve(providerId.ToString()));

        Assert.Equal(ApplicationMessages.DirectoryProviderUnreachable(providerId.ToString()), error.Message);
        encryption.Verify(x => x.Decrypt(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void Resolve_ReturnsDefault_WhenProviderIdIsBlank()
    {
        // The one surviving fallback: no header means no preference.
        var repo = new Mock<IProviderRepository>();
        var defaultProvider = new Mock<ILdapProvider>();
        var factory = BuildFactory(repo, defaultProvider);

        var resolved = factory.Resolve(string.Empty);

        Assert.Same(defaultProvider.Object, resolved);
        repo.Verify(x => x.GetByIdAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public void Resolve_RefusesWith400_WhenProviderIdIsNotAGuidAndNoConfigSectionMatches()
    {
        var repo = new Mock<IProviderRepository>();
        var defaultProvider = new Mock<ILdapProvider>();
        var factory = BuildFactory(repo, defaultProvider);

        Assert.Throws<DomainValidationException>(() => factory.Resolve("not-a-guid-and-not-configured"));

        repo.Verify(x => x.GetByIdAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public void AnUnresolvableDomain_IsNotReProbedOnEveryRequest()
    {
        // Failures are cached briefly. Without that, a request naming a dead domain re-probes
        // every host — a two-second connect timeout each — and repeating the request is enough
        // to tie the server up.
        var providerId = Guid.NewGuid();
        var repo = new Mock<IProviderRepository>();
        repo.Setup(x => x.GetByIdAsync(providerId)).ReturnsAsync((Provider?)null);

        var factory = BuildFactory(repo, new Mock<ILdapProvider>());

        Assert.Throws<DomainValidationException>(() => factory.Resolve(providerId.ToString()));
        Assert.Throws<DomainValidationException>(() => factory.Resolve(providerId.ToString()));
        Assert.Throws<DomainValidationException>(() => factory.Resolve(providerId.ToString()));

        repo.Verify(x => x.GetByIdAsync(providerId), Times.Once);
    }

    [Fact]
    public void AResolvedDomain_IsStillTheOneReturned()
    {
        // The change is about failure. Success must be untouched, and a configured section is
        // the path that does not need a reachable host in a test.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ldap:Providers:branch:Server"] = "127.0.0.1",
                ["Ldap:Providers:branch:Port"] = "389",
                ["Ldap:Providers:branch:BaseDn"] = "dc=branch,dc=example,dc=com"
            })
            .Build();

        var repo = new Mock<IProviderRepository>();
        var defaultProvider = new Mock<ILdapProvider>();
        var factory = BuildFactory(repo, defaultProvider, configuration: configuration);

        // Nothing is listening on 127.0.0.1:389 in the test run, so this resolves to the
        // unreachable outcome rather than to the default — which is the point either way: a
        // named domain never silently becomes the default one.
        Assert.Throws<DirectoryUnavailableException>(() => factory.Resolve("branch"));
    }
}
