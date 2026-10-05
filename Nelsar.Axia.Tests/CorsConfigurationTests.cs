using Nelsar.Axia.Api.Configuration;

namespace Nelsar.Axia.Tests;

/// <summary>
/// What may be written in Cors:AllowedOrigins, and what happens when it is not.
///
/// Each of the refused forms used to be accepted and then match no origin a browser sends. The
/// console reported the API unreachable and nothing pointed at the setting; an empty entry also
/// switched off the loopback default. A <c>*</c> was accepted too, and quietly admitted every
/// website.
/// </summary>
public class CorsConfigurationTests
{
    [Theory]
    [InlineData("https://console.example.com/")]   // copied from the address bar
    [InlineData("https://console.example.com/app")]
    [InlineData("https://console.example.com?x=1")]
    [InlineData("https://console.example.com:443")] // a browser never sends the default port
    [InlineData("https://*.example.com")]
    [InlineData("*")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("console.example.com")]
    [InlineData("ftp://console.example.com")]
    public void AnEntryThatCanNeverMatch_StopsStartup(string entry)
    {
        var options = new CorsOptions { AllowedOrigins = [entry] };

        var error = Assert.Throws<InvalidOperationException>(() => options.ParseAllowedOrigins());

        Assert.Contains("Cors:AllowedOrigins", error.Message);
    }

    [Fact]
    public void ATrailingSlash_IsRefused_WithTheValueToWriteInstead()
    {
        var options = new CorsOptions { AllowedOrigins = ["https://console.example.com/"] };

        var error = Assert.Throws<InvalidOperationException>(() => options.ParseAllowedOrigins());

        Assert.Contains("write https://console.example.com", error.Message);
    }

    [Fact]
    public void AStar_PointsAtTheExplicitSwitch()
    {
        var options = new CorsOptions { AllowedOrigins = ["*"] };

        var error = Assert.Throws<InvalidOperationException>(() => options.ParseAllowedOrigins());

        Assert.Contains("AllowAnyOrigin", error.Message);
    }

    [Theory]
    [InlineData("https://console.example.com", "https://console.example.com")]
    [InlineData("http://localhost:5500", "http://localhost:5500")]
    [InlineData("http://[::1]:5500", "http://[::1]:5500")]
    // Host names are case-insensitive, and browsers send them lower-cased.
    [InlineData("https://Console.Example.com", "https://console.example.com")]
    [InlineData("  https://console.example.com  ", "https://console.example.com")]
    public void ABareOrigin_IsAccepted(string entry, string expected)
    {
        var options = new CorsOptions { AllowedOrigins = [entry] };

        Assert.Equal(expected, Assert.Single(options.ParseAllowedOrigins()));
    }

    [Fact]
    public void TheSwitchAndAList_AreRefusedTogether()
    {
        // The switch already admits everything; a list beside it would read as a restriction
        // that is not there.
        var options = new CorsOptions
        {
            AllowAnyOrigin = true,
            AllowedOrigins = ["https://console.example.com"]
        };

        Assert.Throws<InvalidOperationException>(() => options.ParseAllowedOrigins());
    }

    [Fact]
    public void TheHost_RefusesToStart_OnAnOriginThatCanNeverMatch()
    {
        using var factory = new ApiTestFactory(
            _ => { },
            new Dictionary<string, string?> { ["Cors:AllowedOrigins:0"] = "https://console.example.com/" });

        var failure = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        while (failure is not InvalidOperationException && failure.InnerException is { } inner)
            failure = inner;
        Assert.Contains("Cors:AllowedOrigins", failure.Message);
    }
}
