using Paperbunkr.Sharing.Discovery;

namespace Paperbunkr.Sharing.Tests;

public class ShareDiscoveryTests
{
    [Fact]
    public void Txt_CarriesOnlyWhatHelloAlreadyShows_AndNoSecrets()
    {
        var txt = ShareDiscovery.BuildTxt("abc-123", "Den PC");

        Assert.Equal(new[] { "id", "name", "v" }, txt.Keys.OrderBy(k => k).ToArray());
        Assert.Equal("abc-123", txt["id"]);
        Assert.Equal("Den PC", txt["name"]);
        Assert.Equal(ProtocolVersion.Current.ToString(), txt["v"]);
    }

    [Fact]
    public void ParsesAWellFormedAdvertisement()
    {
        var host = ShareDiscovery.TryParse(new[] { "id=abc-123", "name=Den PC", "v=1" }, "192.168.1.20", 7614);

        Assert.Equal(new DiscoveredHost("abc-123", "Den PC", 1, "192.168.1.20", 7614), host);
    }

    [Fact]
    public void FallsBackToTheAddress_WhenThereIsNoName()
    {
        Assert.Equal("10.0.0.5", ShareDiscovery.TryParse(new[] { "id=x", "v=1" }, "10.0.0.5", 7614)!.DisplayName);
    }

    [Theory]
    [InlineData("name=No Id", "v=1")]
    [InlineData("id=x", "name=No Version")]
    [InlineData("id=", "v=1")]
    [InlineData("id=x", "v=notanumber")]
    public void RejectsAdvertisementsThatAreNotUsable(string a, string b)
    {
        Assert.Null(ShareDiscovery.TryParse(new[] { a, b }, "10.0.0.5", 7614));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(70000)]
    public void RejectsImpossiblePorts(int port)
    {
        Assert.Null(ShareDiscovery.TryParse(new[] { "id=x", "v=1" }, "10.0.0.5", port));
    }

    [Fact]
    public void AnEmptyAddressIsRejected()
    {
        Assert.Null(ShareDiscovery.TryParse(new[] { "id=x", "v=1" }, " ", 7614));
    }

    [Fact]
    public async Task Advertiser_NeverThrows_EvenWithOddNames_AndBrowseOnlyReturnsWhatIsReallyThere()
    {
        string id = Guid.NewGuid().ToString();
        string odd = "Den.PC" + (char)7 + " with a very very very very very long display name indeed";
        using var advertiser = new ShareDiscovery.Advertiser(id, odd, 7614);

        var hosts = await ShareDiscovery.BrowseAsync(TimeSpan.FromSeconds(4));

        // Multicast may be blocked here (firewall, CI). When we do see ourselves the content must be exactly right;
        // otherwise there is nothing to assert - discovery is a convenience, manual entry is the baseline.
        var me = hosts.FirstOrDefault(h => h.InstanceId == id);
        if (me is not null)
        {
            Assert.Equal(7614, me.Port);
            Assert.Equal(ProtocolVersion.Current, me.ProtocolVersion);
            Assert.False(string.IsNullOrWhiteSpace(me.Address));
        }
    }

    [Fact]
    public async Task Browse_HonoursCancellation_AndReturnsPromptly()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var watch = System.Diagnostics.Stopwatch.StartNew();

        await ShareDiscovery.BrowseAsync(TimeSpan.FromSeconds(30), cts.Token);

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5));
    }
}
