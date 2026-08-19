using LocalSendDotNet;

namespace LocalSendDotNet.Core.Tests;

public sealed class NetworkAddressPatternsTests
{
    [Fact]
    public void NullListsDoNotIgnoreInterfaces()
    {
        Assert.False(NetworkAddressPatterns.IsInterfaceIgnored(["192.168.1.20"], null, null));
    }

    [Fact]
    public void EmptyWhitelistIgnoresEveryInterface()
    {
        Assert.True(NetworkAddressPatterns.IsInterfaceIgnored(["192.168.1.20"], [""], null));
        Assert.True(NetworkAddressPatterns.IsInterfaceIgnored(["192.168.1.20"], [], null));
    }

    [Fact]
    public void WhitelistKeepsMatchingInterface()
    {
        Assert.False(NetworkAddressPatterns.IsInterfaceIgnored(["192.168.1.20"], ["192.168.1.*"], null));
        Assert.True(NetworkAddressPatterns.IsInterfaceIgnored(["10.0.0.8"], ["192.168.1.*"], null));
    }

    [Fact]
    public void BlacklistIgnoresMatchingInterface()
    {
        Assert.True(NetworkAddressPatterns.IsInterfaceIgnored(["10.0.0.8"], null, ["10.0.0.*"]));
        Assert.False(NetworkAddressPatterns.IsInterfaceIgnored(["192.168.1.20"], null, ["10.0.0.*"]));
    }

    [Fact]
    public void BlacklistIgnoresWholeInterfaceWhenAnyAddressMatches()
    {
        Assert.True(NetworkAddressPatterns.IsInterfaceIgnored(["192.168.1.20", "10.0.0.8"], null, ["10.0.0.8"]));
    }

    [Fact]
    public void WhitelistTakesPrecedenceOverBlacklist()
    {
        Assert.False(NetworkAddressPatterns.IsInterfaceIgnored(["192.168.1.20"], ["192.168.1.20"], ["192.168.1.20"]));
        Assert.True(NetworkAddressPatterns.IsInterfaceIgnored(["10.0.0.8"], ["192.168.1.20"], ["10.0.0.8"]));
    }

    [Theory]
    [InlineData("192.168.1.20", "192.168.1.20", true)]
    [InlineData("192.168.1.*", "192.168.1.20", true)]
    [InlineData("192.168.*.20", "192.168.9.20", true)]
    [InlineData("192.168.1.*", "192.168.2.20", false)]
    [InlineData("192.168.1.20", "192.168.1.21", false)]
    [InlineData("", "192.168.1.20", false)]
    [InlineData("192.168.1", "192.168.1.20", false)]
    [InlineData("not-an-ip", "192.168.1.20", false)]
    public void MatchesDottedQuadPatterns(string pattern, string address, bool expected) =>
        Assert.Equal(expected, NetworkAddressPatterns.Matches(pattern, address));
}
