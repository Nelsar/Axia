using System.Net;
using Nelsar.Axia.Api.Configuration;

namespace Nelsar.Axia.Tests;

/// <summary>
/// The proxy list decides whose X-Forwarded-For is believed, and the believed address is what the
/// rate limiter partitions by. These are about the entries that cannot be read: an instance that
/// skipped them would look configured and behave as though no proxy had been named.
/// </summary>
public class ReverseProxyOptionsTests
{
    [Fact]
    public void NoProxyNamed_IsNotConfigured()
    {
        var options = new ReverseProxyOptions();

        Assert.False(options.IsConfigured);
    }

    [Theory]
    [InlineData("10.0.0.7")]
    [InlineData("::1")]
    [InlineData("2001:db8::1")]
    public void AnAddress_IsRead(string proxy)
    {
        var options = new ReverseProxyOptions { KnownProxies = [proxy] };

        Assert.True(options.IsConfigured);
        Assert.Equal([IPAddress.Parse(proxy)], options.ParseKnownProxies());
    }

    [Theory]
    // A host name is the plausible mistake: it reads like an address and cannot be one here,
    // because the check runs per request against the peer the connection came from.
    [InlineData("proxy.example.com")]
    [InlineData("10.0.0.7:8080")]
    [InlineData("10.0.0.0/8")]
    [InlineData("")]
    [InlineData("   ")]
    public void AnUnreadableProxy_StopsStartup(string proxy)
    {
        var options = new ReverseProxyOptions { KnownProxies = [proxy] };

        var error = Assert.Throws<InvalidOperationException>(() => options.ParseKnownProxies());

        // The operator has to be able to find the entry, so the message names both the setting
        // and the value as written.
        Assert.Contains("ReverseProxy:KnownProxies", error.Message);
        Assert.Contains(proxy, error.Message);
    }

    [Theory]
    [InlineData("10.0.0.0/8")]
    [InlineData("172.16.0.0/12")]
    [InlineData("2001:db8::/32")]
    public void ANetwork_IsRead(string network)
    {
        var options = new ReverseProxyOptions { KnownNetworks = [network] };

        Assert.True(options.IsConfigured);
        Assert.Equal([IPNetwork.Parse(network)], options.ParseKnownNetworks());
    }

    [Theory]
    [InlineData("10.0.0.0")]
    [InlineData("10.0.0.0/33")]
    [InlineData("10.0.0.0/-1")]
    [InlineData("not-a-network")]
    public void AnUnreadableNetwork_StopsStartup(string network)
    {
        var options = new ReverseProxyOptions { KnownNetworks = [network] };

        var error = Assert.Throws<InvalidOperationException>(() => options.ParseKnownNetworks());

        Assert.Contains("ReverseProxy:KnownNetworks", error.Message);
        Assert.Contains(network, error.Message);
    }

    /// <summary>
    /// IPNetwork.TryParse accepts 10.0.0.7/8 and hands back 10.0.0.0/8, masking the host bits off.
    /// Taking that silently would trust sixteen million addresses on the strength of a typo, so
    /// the ambiguity is refused and the message offers both readings.
    /// </summary>
    [Theory]
    [InlineData("10.0.0.7/8", "10.0.0.7", "10.0.0.0/8")]
    [InlineData("192.168.1.4/24", "192.168.1.4", "192.168.1.0/24")]
    [InlineData("2001:db8::1/32", "2001:db8::1", "2001:db8::/32")]
    public void ANetworkWithHostBitsSet_IsRefusedRatherThanMasked(
        string network,
        string singleAddress,
        string wholeNetwork)
    {
        var options = new ReverseProxyOptions { KnownNetworks = [network] };

        var error = Assert.Throws<InvalidOperationException>(() => options.ParseKnownNetworks());

        Assert.Contains(singleAddress, error.Message);
        Assert.Contains(wholeNetwork, error.Message);
        // Naming the other setting is the actionable half: a single address belongs there.
        Assert.Contains(nameof(ReverseProxyOptions.KnownProxies), error.Message);
    }

    [Fact]
    public void TheFirstUnreadableEntry_IsTheOneReported()
    {
        var options = new ReverseProxyOptions { KnownProxies = ["10.0.0.7", "nonsense", "also-nonsense"] };

        var error = Assert.Throws<InvalidOperationException>(() => options.ParseKnownProxies());

        Assert.Contains("nonsense", error.Message);
        Assert.DoesNotContain("also-nonsense", error.Message);
    }
}
