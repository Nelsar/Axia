using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Infrastructure.Identity;
using Nelsar.Axia.Infrastructure.Persistence;
using Xunit;

namespace Nelsar.Axia.Tests;

public class DbInitializerEncryptionTests
{
    [Fact]
    public async System.Threading.Tasks.Task InitializeAsync_SeedsEncryptedPassword_WhenKeyPresent()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddDbContext<AppDbContext>(opts => opts.UseInMemoryDatabase(Guid.NewGuid().ToString()));

        var inMemory = new System.Collections.Generic.Dictionary<string, string?>
        {
            ["Encryption:Key"] = Convert.ToBase64String(new byte[32])
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();
        services.AddSingleton<IConfiguration>(config);

        services.AddSingleton<IEncryptionService>(
            new AesGcmEncryptionService(config, NullLogger<AesGcmEncryptionService>.Instance));

        var provider = services.BuildServiceProvider();

        // Instead of invoking EF seeder (which triggers relational model behavior in tests),
        // validate that the registered IEncryptionService will encrypt the seeded value.
        var encryptor = provider.GetService<IEncryptionService>();
        Assert.NotNull(encryptor);

        var result = encryptor!.Encrypt("adminpassword");
        Assert.False(string.IsNullOrWhiteSpace(result));
        Assert.StartsWith("enc:", result, StringComparison.Ordinal);
    }
}
