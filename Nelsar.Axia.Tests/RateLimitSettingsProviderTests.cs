using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Domain.Repositories;
using Nelsar.Axia.Infrastructure.Persistence.Repositories;
using Nelsar.Axia.Infrastructure.RateLimiting;

namespace Nelsar.Axia.Tests;

public class RateLimitSettingsProviderTests
{
    [Fact]
    public void GetSettings_ReturnsFallbackDefaults_WhenNoRowConfigured()
    {
        var repo = new Mock<IRateLimitSettingsRepository>();
        repo.Setup(x => x.GetByPolicyNameAsync("auth-login")).ReturnsAsync((RateLimitSettings?)null);

        var provider = new RateLimitSettingsProvider(repo.Object, new MemoryCache(new MemoryCacheOptions()), NullLogger<RateLimitSettingsProvider>.Instance);

        var settings = provider.GetSettings("auth-login");

        Assert.True(settings.Enabled);
        Assert.Equal(5, settings.PermitLimit);
        Assert.Equal(60, settings.WindowSeconds);
    }

    [Fact]
    public void GetSettings_ReturnsConfiguredValues_WhenRowExists()
    {
        var repo = new Mock<IRateLimitSettingsRepository>();
        repo.Setup(x => x.GetByPolicyNameAsync("auth-login"))
            .ReturnsAsync(RateLimitSettings.Create("auth-login", 20, 30, 1, true));

        var provider = new RateLimitSettingsProvider(repo.Object, new MemoryCache(new MemoryCacheOptions()), NullLogger<RateLimitSettingsProvider>.Instance);

        var settings = provider.GetSettings("auth-login");

        Assert.Equal(20, settings.PermitLimit);
        Assert.Equal(30, settings.WindowSeconds);
        Assert.Equal(1, settings.QueueLimit);
    }

    [Fact]
    public void GetSettings_HitsRepositoryOnlyOnce_WithinCacheTtl()
    {
        var repo = new Mock<IRateLimitSettingsRepository>();
        repo.Setup(x => x.GetByPolicyNameAsync("auth-login"))
            .ReturnsAsync(RateLimitSettings.Create("auth-login", 5, 60, 0, true));

        var provider = new RateLimitSettingsProvider(repo.Object, new MemoryCache(new MemoryCacheOptions()), NullLogger<RateLimitSettingsProvider>.Instance);

        provider.GetSettings("auth-login");
        provider.GetSettings("auth-login");
        provider.GetSettings("auth-login");

        repo.Verify(x => x.GetByPolicyNameAsync("auth-login"), Times.Once);
    }

    [Fact]
    public void GetSettings_ReturnsFallback_WhenRepositoryThrows()
    {
        var repo = new Mock<IRateLimitSettingsRepository>();
        repo.Setup(x => x.GetByPolicyNameAsync("auth-login")).ThrowsAsync(new InvalidOperationException("db unavailable"));

        var provider = new RateLimitSettingsProvider(repo.Object, new MemoryCache(new MemoryCacheOptions()), NullLogger<RateLimitSettingsProvider>.Instance);

        var settings = provider.GetSettings("auth-login");

        Assert.True(settings.Enabled);
        Assert.Equal(5, settings.PermitLimit);
    }
}
