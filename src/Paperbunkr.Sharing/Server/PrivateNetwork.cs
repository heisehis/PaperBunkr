using System.Net;
using System.Net.Sockets;

namespace Paperbunkr.Sharing.Server;

/// <summary>
/// "Is this client on a network the host would call private?" - the LAN/VPN-only stance in spec §1.
/// Loopback, RFC 1918, link-local, IPv6 unique-local, and 100.64.0.0/10 (carrier-grade NAT space,
/// which Tailscale and similar VPNs assign) count; anything else is a public address.
/// </summary>
public static class PrivateNetwork
{
    public static bool IsPrivateOrLoopback(IPAddress? address)
    {
        if (address is null)
        {
            return false;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        byte[] b = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return b[0] == 10
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 169 && b[1] == 254)
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127);
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal || (b[0] & 0xFE) == 0xFC;
        }

        return false;
    }
}
