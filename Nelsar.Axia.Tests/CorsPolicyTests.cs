using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;

namespace Nelsar.Axia.Tests;

/// <summary>
/// Which browser origins the API answers.
///
/// This shipped as AllowAnyOrigin, on the reasoning that authentication is a bearer token rather
/// than a cookie, so no browser would attach credentials on another site's behalf. That is true
/// and it is not the whole risk: any page a person visits could reach an API they can route to and
/// read what it answers without a token. These pin the default so it cannot drift back.
/// </summary>
public class CorsPolicyTests
{
    private const string AllowOrigin = "Access-Control-Allow-Origin";

    private static async Task<HttpResponseMessage> CallWithOrigin(
        ApiTestFactory factory, string origin)
    {
        var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/providers/selectable");
        request.Headers.Add("Origin", origin);
        return await client.SendAsync(request);
    }

    [Theory]
    [InlineData("http://localhost:5500")]   // the console under `npm run dev`
    [InlineData("http://localhost:5173")]   // whichever port Vite picks instead
    [InlineData("http://127.0.0.1:8080")]
    public async Task WithNothingConfigured_TheConsoleOnThisMachineIsAllowed(string origin)
    {
        // Local development has to work without an operator editing configuration first.
        using var factory = new ApiTestFactory(_ => { });

        var response = await CallWithOrigin(factory, origin);

        Assert.True(response.Headers.Contains(AllowOrigin),
            $"{origin} should be allowed when no origins are configured.");
    }

    [Theory]
    [InlineData("https://evil.example.com")]
    [InlineData("http://attacker.test")]
    [InlineData("https://localhost.evil.example.com")]   // not loopback, however it reads
    public async Task WithNothingConfigured_AnyOtherSiteIsRefused(string origin)
    {
        // The default this class exists for. A page the administrator happens to visit must not be
        // able to read what this API answers, even unauthenticated: which directories exist, and
        // whether the host is reachable at all, is more than a stranger should learn.
        using var factory = new ApiTestFactory(_ => { });

        var response = await CallWithOrigin(factory, origin);

        Assert.False(response.Headers.Contains(AllowOrigin),
            $"{origin} must not be allowed when no origins are configured.");
    }

    [Fact]
    public async Task AConfiguredOrigin_IsAllowed_AndOthersStillAreNot()
    {
        using var factory = new ApiTestFactory(
            _ => { },
            new Dictionary<string, string?>
            {
                ["Cors:AllowedOrigins:0"] = "https://console.example.com"
            });

        var allowed = await CallWithOrigin(factory, "https://console.example.com");
        var refused = await CallWithOrigin(factory, "https://evil.example.com");

        Assert.True(allowed.Headers.Contains(AllowOrigin));
        Assert.False(refused.Headers.Contains(AllowOrigin));
    }

    [Fact]
    public async Task ConfiguringOrigins_ClosesTheLoopbackDefault()
    {
        // Naming origins is an operator saying "these and no others". Keeping loopback allowed on
        // top of that would make the list advisory.
        using var factory = new ApiTestFactory(
            _ => { },
            new Dictionary<string, string?>
            {
                ["Cors:AllowedOrigins:0"] = "https://console.example.com"
            });

        var response = await CallWithOrigin(factory, "http://localhost:5500");

        Assert.False(response.Headers.Contains(AllowOrigin));
    }

    [Fact]
    public async Task TheCorrelationIdStaysReadableFromScript()
    {
        // Exposed deliberately: without it the console cannot read the id off a failed call, and
        // the id stops being something a person can quote when reporting the failure.
        using var factory = new ApiTestFactory(_ => { });

        var response = await CallWithOrigin(factory, "http://localhost:5500");

        Assert.Contains("X-Correlation-ID", response.Headers.GetValues("Access-Control-Expose-Headers").First());
    }

    [Fact]
    public async Task TheExplicitSwitch_AdmitsAnyOrigin()
    {
        using var factory = new ApiTestFactory(
            _ => { },
            new Dictionary<string, string?> { ["Cors:AllowAnyOrigin"] = "true" });

        var response = await CallWithOrigin(factory, "https://anywhere.example.com");

        Assert.Equal("*", response.Headers.GetValues(AllowOrigin).Single());
    }

    [Fact]
    public async Task APreflight_IsCachedByTheBrowser()
    {
        // Every console call carries Authorization and so is preflighted. Without a max age the
        // browser keeps the answer for about five seconds, and nearly every request went twice.
        using var factory = new ApiTestFactory(_ => { });
        var client = factory.CreateClient();

        var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/v1/users");
        preflight.Headers.Add("Origin", "http://localhost:5500");
        preflight.Headers.Add("Access-Control-Request-Method", "GET");
        preflight.Headers.Add("Access-Control-Request-Headers", "authorization");

        var response = await client.SendAsync(preflight);

        Assert.Equal("600", response.Headers.GetValues("Access-Control-Max-Age").Single());
    }
}
