using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Nelsar.Axia.Application.Configuration;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Infrastructure.Identity;

namespace Nelsar.Axia.Tests;

/// <summary>
/// The token settings that are not secret, and what an instance does when nobody sets them.
///
/// Issuer and Audience defaulted to empty and the lifetime to 0. An instance configured through
/// the environment alone — a container — issued tokens that it then refused itself: signing in
/// worked, and every request after it was answered 401. The lifetime, meanwhile, was five minutes
/// of clock skew rather than the hour the README promised.
/// </summary>
public class JwtSettingsTests
{
    private const string Key = "0123456789abcdef0123456789abcdef-test-only";

    [Fact]
    public void Unset_TheyMatchWhatTheReadmeSays()
    {
        var settings = new JwtSettings();

        Assert.Equal("Nelsar.Axia.Api", settings.Issuer);
        Assert.Equal("Nelsar.Axia.Api", settings.Audience);
        Assert.Equal(60, settings.ExpiresInMinutes);
    }

    [Theory]
    [InlineData("", "Nelsar.Axia.Api", 60, "Jwt:Issuer")]
    [InlineData("   ", "Nelsar.Axia.Api", 60, "Jwt:Issuer")]
    [InlineData("Nelsar.Axia.Api", "", 60, "Jwt:Audience")]
    [InlineData("Nelsar.Axia.Api", "Nelsar.Axia.Api", 0, "Jwt:ExpiresInMinutes")]
    [InlineData("Nelsar.Axia.Api", "Nelsar.Axia.Api", -5, "Jwt:ExpiresInMinutes")]
    public void ASettingThatMakesEveryTokenUnusable_StopsStartup(string issuer, string audience, int minutes, string named)
    {
        var problem = JwtSettingsValidation.Problem(new JwtSettings
        {
            Key = Key,
            Issuer = issuer,
            Audience = audience,
            ExpiresInMinutes = minutes
        });

        Assert.NotNull(problem);
        Assert.StartsWith(named, problem);
    }

    [Fact]
    public void AnyNamesAndAPositiveLifetime_AreFine()
    {
        Assert.Null(JwtSettingsValidation.Problem(new JwtSettings
        {
            Key = Key,
            Issuer = "axia.example.com",
            Audience = "axia-console",
            ExpiresInMinutes = 15
        }));
    }

    [Fact]
    public async Task WithNothingButAKey_ATokenThisInstanceIssues_IsOneItAccepts()
    {
        // ApiTestFactory configures the key and nothing else about tokens — what a container gets.
        using var factory = new ApiTestFactory(_ => { });
        var client = factory.CreateClient();

        var token = factory.Services.GetRequiredService<ITokenService>().GenerateToken(new UserDto
        {
            Id = Guid.NewGuid(),
            Email = "ivan.petrov@example.com",
            Username = "ivan.petrov",
            Name = "Ivan Petrov",
            Role = UserRoles.Admin,
            Dn = "uid=ivan.petrov,ou=people,dc=example,dc=com"
        });

        var lifetime = new JwtSecurityTokenHandler().ReadJwtToken(token).ValidTo - DateTime.UtcNow;
        Assert.InRange(lifetime, TimeSpan.FromMinutes(59), TimeSpan.FromMinutes(61));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.GetAsync("/api/v1/rate-limit-settings");

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
