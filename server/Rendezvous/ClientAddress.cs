using System.Net;
using System.Net.Sockets;

namespace MindAttic.Unity.Multiplayer.Rendezvous;

/// <summary>Who a request came from, for rate limiting and for registrations that omit their address.</summary>
internal static class ClientAddress
{
    /// <summary>The caller's IP (forwarded-header aware once that middleware ran), IPv4-mapped IPv6 unwrapped.</summary>
    public static IPAddress? Of(HttpContext context)
    {
        IPAddress? ip = context.Connection.RemoteIpAddress;
        return ip != null && ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;
    }

    /// <summary>
    /// The rate-limit partition for a request: the IPv4 address, or the /64 prefix of an IPv6 one
    /// (one subscriber usually owns a whole /64, so per-address limits there would be trivial to dodge).
    /// </summary>
    public static string PartitionKey(HttpContext context)
    {
        IPAddress? ip = Of(context);
        if (ip == null)
        {
            return "unknown";
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            byte[] bytes = ip.GetAddressBytes();
            Array.Clear(bytes, 8, 8);
            return new IPAddress(bytes) + "/64";
        }

        return ip.ToString();
    }
}
