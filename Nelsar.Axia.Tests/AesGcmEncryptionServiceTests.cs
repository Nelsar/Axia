using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Nelsar.Axia.Infrastructure.Identity;
using Xunit;

namespace Nelsar.Axia.Tests;

public class AesGcmEncryptionServiceTests
{
    [Fact]
    public void Encrypt_Then_Decrypt_ReturnsOriginal_WhenKeyConfigured()
    {
        var inMemory = new System.Collections.Generic.Dictionary<string, string?>
        {
            ["Encryption:Key"] = Convert.ToBase64String(new byte[32])
        };

        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();

        var type = Type.GetType("Nelsar.Axia.Infrastructure.Identity.AesGcmEncryptionService, Nelsar.Axia.Infrastructure");
        Assert.NotNull(type);
        var loggerType = typeof(NullLogger<>).MakeGenericType(type);
        var loggerInstance = Activator.CreateInstance(loggerType);
        var svc = Activator.CreateInstance(type!, config, loggerInstance);

        var secret = "TopSecret123!";
        var encryptMethod = type!.GetMethod("Encrypt");
        var decryptMethod = type.GetMethod("Decrypt");
        Assert.NotNull(encryptMethod);
        Assert.NotNull(decryptMethod);

        var encrypted = (string)encryptMethod.Invoke(svc, new object[] { secret })!;

        Assert.NotNull(encrypted);
        Assert.StartsWith("enc:", encrypted);

        var decrypted = (string)decryptMethod.Invoke(svc, new object[] { encrypted })!;
        Assert.Equal(secret, decrypted);
    }

    [Fact]
    public void Encrypt_Refuses_WhenKeyNotConfigured()
    {
        // It used to hand the plaintext back, which the caller stored as if it were encrypted:
        // a missing key put every bind password in the database in the clear.
        var config = new ConfigurationBuilder().Build();

        var type = Type.GetType("Nelsar.Axia.Infrastructure.Identity.AesGcmEncryptionService, Nelsar.Axia.Infrastructure");
        Assert.NotNull(type);
        var loggerType = typeof(NullLogger<>).MakeGenericType(type);
        var loggerInstance = Activator.CreateInstance(loggerType);
        var svc = Activator.CreateInstance(type!, config, loggerInstance);

        var secret = "plain";
        var encryptMethod = type!.GetMethod("Encrypt");
        Assert.NotNull(encryptMethod);

        var thrown = Assert.Throws<System.Reflection.TargetInvocationException>(
            () => encryptMethod.Invoke(svc, new object[] { secret }));

        Assert.IsType<InvalidOperationException>(thrown.InnerException);
    }
}
