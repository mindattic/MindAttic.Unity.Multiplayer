using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Unity.Multiplayer.Hosting.Cli.Local;

/// <summary>
/// Finds this machine's own LAN IPv4 addresses, so <c>mp-host local up</c> can tell the operator
/// what to give other players on the same network ("LAN join: connect by IP to 192.168.1.42:7777")
/// without them having to look it up themselves.
/// </summary>
public static class LanAddresses
{
    /// <summary>
    /// Every up, non-loopback network interface's IPv4 unicast address, in Windows' adapter order
    /// (physical Ethernet/Wi-Fi adapters are listed before virtual ones in practice, though this
    /// makes no hard guarantee -- an operator with more than one active adapter should pick the one
    /// matching their actual LAN).
    /// </summary>
    public static IReadOnlyList<string> GetLocalIPv4Addresses()
    {
        var addresses = new List<string>();
        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;

            foreach (UnicastIPAddressInformation unicast in nic.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                if (IPAddress_IsLoopback(unicast.Address)) continue;
                addresses.Add(unicast.Address.ToString());
            }
        }

        return addresses;
    }

    private static bool IPAddress_IsLoopback(System.Net.IPAddress address) => System.Net.IPAddress.IsLoopback(address);
}
