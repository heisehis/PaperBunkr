using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Paperbunkr.Sharing.Server;

namespace Paperbunkr.Sharing.Discovery;

/// <summary>One network adapter as <see cref="LanAddresses"/> sees it (a snapshot, so the choice can be tested without real hardware).</summary>
public sealed record NicSnapshot(
    string Name,
    string Description,
    NetworkInterfaceType Type,
    bool IsUp,
    bool HasGateway,
    IReadOnlyList<IPAddress> IPv4Addresses);

/// <summary>
/// The addresses other computers on the LAN can actually reach this one on. Windows reports an address for every adapter -
/// Hyper-V / WSL switches, VirtualBox and VMware host-only adapters, Docker, Bluetooth, self-assigned 169.254 - and the mDNS
/// library advertised all of them, while the browsing side takes the first IPv4 it receives. On a laptop that was often a
/// virtual switch nobody else can reach, so "Add remote library" pre-filled an address that could not connect. The same list
/// is shown in the host's Sharing settings so the user has the right address to type.
/// </summary>
public static class LanAddresses
{
    private static readonly string[] VirtualMarkers =
    {
        "Hyper-V", "vEthernet", "VirtualBox", "VMware", "WSL", "Docker", "Npcap", "Bluetooth", "Virtual Adapter", "Loopback",
    };

    /// <summary>
    /// Real, up, non-virtual adapters with a private IPv4 address. Adapters with a default gateway (the normal home/office LAN) come
    /// first and are the only ones returned when there are any; otherwise every remaining candidate (a direct cable, a VPN) is returned.
    /// </summary>
    public static IReadOnlyList<IPAddress> Select(IEnumerable<NicSnapshot> adapters)
    {
        var candidates = adapters
            .Where(n => n.IsUp && n.Type is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
            .Where(n => !IsVirtual(n))
            .SelectMany(n => n.IPv4Addresses
                .Where(IsUsable)
                .Select(a => (Address: a, n.HasGateway, Rank: TypeRank(n.Type))))
            .ToList();

        var withGateway = candidates.Where(c => c.HasGateway).ToList();
        var chosen = withGateway.Count > 0 ? withGateway : candidates;
        return chosen.OrderBy(c => c.Rank).Select(c => c.Address).Distinct().ToList();
    }

    /// <summary><see cref="Select"/> over this machine's adapters. Never throws: an empty list just means "no address to suggest".</summary>
    public static IReadOnlyList<IPAddress> Current()
    {
        try
        {
            return Select(NetworkInterface.GetAllNetworkInterfaces().Select(Snapshot));
        }
        catch (Exception ex) when (ex is NetworkInformationException or PlatformNotSupportedException or InvalidOperationException)
        {
            return Array.Empty<IPAddress>();
        }
    }

    private static NicSnapshot Snapshot(NetworkInterface nic)
    {
        IPInterfaceProperties props = nic.GetIPProperties();
        bool hasGateway = props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
        var v4 = props.UnicastAddresses.Select(u => u.Address).Where(a => a.AddressFamily == AddressFamily.InterNetwork).ToList();
        return new NicSnapshot(nic.Name, nic.Description, nic.NetworkInterfaceType, nic.OperationalStatus == OperationalStatus.Up, hasGateway, v4);
    }

    private static bool IsVirtual(NicSnapshot nic) =>
        VirtualMarkers.Any(m => nic.Name.Contains(m, StringComparison.OrdinalIgnoreCase) || nic.Description.Contains(m, StringComparison.OrdinalIgnoreCase));

    private static bool IsUsable(IPAddress address)
    {
        byte[] b = address.GetAddressBytes();
        bool selfAssigned = b[0] == 169 && b[1] == 254;
        return !IPAddress.IsLoopback(address) && !selfAssigned && PrivateNetwork.IsPrivateOrLoopback(address);
    }

    private static int TypeRank(NetworkInterfaceType type) => type switch
    {
        NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT => 0,
        NetworkInterfaceType.Wireless80211 => 1,
        _ => 2,
    };
}
