using System.Net;
using System.Net.NetworkInformation;
using Paperbunkr.Sharing.Discovery;

namespace Paperbunkr.Sharing.Tests;

public class LanAddressesTests
{
    private static NicSnapshot Nic(string name, string address, bool gateway = true, NetworkInterfaceType type = NetworkInterfaceType.Wireless80211,
        bool up = true, string? description = null) =>
        new(name, description ?? name, type, up, gateway, new[] { IPAddress.Parse(address) });

    [Fact]
    public void TypicalLaptop_PicksTheWiFiAddress_NotTheVirtualSwitches()
    {
        var picked = LanAddresses.Select(new[]
        {
            Nic("vEthernet (WSL)", "172.28.80.1", gateway: false, type: NetworkInterfaceType.Ethernet, description: "Hyper-V Virtual Ethernet Adapter"),
            Nic("vEthernet (Default Switch)", "172.17.0.1", gateway: false, type: NetworkInterfaceType.Ethernet, description: "Hyper-V Virtual Ethernet Adapter #2"),
            Nic("VirtualBox Host-Only Network", "192.168.56.1", gateway: false, type: NetworkInterfaceType.Ethernet,
                description: "VirtualBox Host-Only Ethernet Adapter"),
            Nic("Wi-Fi", "192.168.1.23"),
        });

        Assert.Equal(new[] { IPAddress.Parse("192.168.1.23") }, picked);
    }

    [Fact]
    public void SelfAssignedPublicAndDownAdaptersAreSkipped()
    {
        var picked = LanAddresses.Select(new[]
        {
            Nic("Ethernet", "169.254.10.4", type: NetworkInterfaceType.Ethernet),
            Nic("Ethernet 2", "8.8.4.4", type: NetworkInterfaceType.Ethernet),
            Nic("Wi-Fi 2", "192.168.0.9", up: false),
            Nic("Wi-Fi", "10.0.0.15"),
        });

        Assert.Equal(new[] { IPAddress.Parse("10.0.0.15") }, picked);
    }

    [Fact]
    public void WiredComesBeforeWireless()
    {
        var picked = LanAddresses.Select(new[]
        {
            Nic("Wi-Fi", "192.168.1.23"),
            Nic("Ethernet", "192.168.1.40", type: NetworkInterfaceType.Ethernet),
        });

        Assert.Equal(new[] { IPAddress.Parse("192.168.1.40"), IPAddress.Parse("192.168.1.23") }, picked);
    }

    [Fact]
    public void NoGatewayAnywhere_FallsBackToEveryRealCandidate()
    {
        // A direct cable between two PCs, or only a VPN: no adapter has a gateway, but the address still works.
        var picked = LanAddresses.Select(new[] { Nic("Ethernet", "192.168.2.1", gateway: false, type: NetworkInterfaceType.Ethernet) });

        Assert.Equal(new[] { IPAddress.Parse("192.168.2.1") }, picked);
    }

    [Fact]
    public void LoopbackAndTunnelAdaptersAreSkipped()
    {
        var picked = LanAddresses.Select(new[]
        {
            Nic("Loopback Pseudo-Interface 1", "127.0.0.1", type: NetworkInterfaceType.Loopback),
            Nic("Teredo", "10.9.9.9", type: NetworkInterfaceType.Tunnel),
        });

        Assert.Empty(picked);
    }

    [Theory]
    [InlineData("192.168.1.23", "169.254.3.3")]
    [InlineData("192.168.1.23", "fe80::1")]
    [InlineData("10.0.0.2", "8.8.8.8")]
    public void Browsing_PrefersThePrivateIPv4Address(string better, string worse) =>
        Assert.True(ShareDiscovery.AddressPreference(IPAddress.Parse(better)) < ShareDiscovery.AddressPreference(IPAddress.Parse(worse)));
}
