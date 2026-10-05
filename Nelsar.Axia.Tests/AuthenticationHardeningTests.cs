using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Moq;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Infrastructure.Identity;

namespace Nelsar.Axia.Tests;

/// <summary>
/// The two ways in that should not have existed: LDAP credentials in a Basic header, and tokens
/// signed with a key anybody could read.
/// </summary>
public class AuthenticationHardeningTests
{
    // ---- Basic authentication is gone ----

    [Fact]
    public async Task BasicCredentials_AreNotAcceptedAnywhere()
    {
        // The scheme checked the password against the default directory and then served the
        // request from whichever directory the header named, outside the sign-in rate limit.
        // Now a Basic header is simply not a credential: the directory is never asked.
        var ldap = new Mock<ILdapService>();

        using var factory = new ApiTestFactory(services =>
        {
            services.RemoveAll(typeof(ILdapService));
            services.AddSingleton(ldap.Object);
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:password")));

        var response = await client.GetAsync("/api/v1/users");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        ldap.Verify(x => x.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task OnlyTheBearerScheme_IsRegistered()
    {
        using var factory = new ApiTestFactory(_ => { });

        var schemes = await factory.Services.GetRequiredService<IAuthenticationSchemeProvider>().GetAllSchemesAsync();

        Assert.Equal("Bearer", Assert.Single(schemes).Name);
    }

    // ---- Secrets that cannot be trusted stop the host ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("CHANGE_ME_AT_LEAST_32_CHARACTERS_LONG")] // the example configuration's own value
    [InlineData("too-short-to-sign-with")]
    public void AnUnusableSigningKey_IsNamed(string? key)
    {
        Assert.NotNull(SecretRequirements.JwtKeyProblem(key));
    }

    [Fact]
    public void AGeneratedSigningKey_IsAccepted()
    {
        Assert.Null(SecretRequirements.JwtKeyProblem(Convert.ToBase64String(new byte[48])));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("CHANGE_ME_BASE64_16_24_OR_32_BYTE_AES_KEY")] // the example configuration's own value
    [InlineData("not base64 at all!")]
    [InlineData("AAAAAAAAAAAAAAAAAAAA")] // valid base64, 15 bytes: not an AES key length
    public void AnUnusableEncryptionKey_IsNamed(string? key)
    {
        Assert.NotNull(SecretRequirements.EncryptionKeyProblem(key));
    }

    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(32)]
    public void AnAesKey_IsAccepted(int bytes)
    {
        Assert.Null(SecretRequirements.EncryptionKeyProblem(Convert.ToBase64String(new byte[bytes])));
    }

    [Theory]
    [InlineData("Jwt:Key", "CHANGE_ME_AT_LEAST_32_CHARACTERS_LONG")]
    [InlineData("Encryption:Key", "CHANGE_ME_BASE64_16_24_OR_32_BYTE_AES_KEY")]
    [InlineData("Encryption:Key", "")]
    public void TheHost_RefusesToStart_WithAnUnusableKey(string setting, string value)
    {
        // Copying the example configuration as it stands used to give a host that signed tokens
        // with a published key and stored bind passwords in plaintext. Now it gives no host.
        using var factory = new ApiTestFactory(_ => { }).WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?> { [setting] = value })));

        var failure = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        var validation = Assert.IsType<OptionsValidationException>(Unwrap(failure));
        Assert.Contains(setting, string.Join(" ", validation.Failures));
    }

    private static Exception Unwrap(Exception exception)
    {
        while (exception is not OptionsValidationException && exception.InnerException is { } inner)
            exception = inner;
        return exception;
    }
}
