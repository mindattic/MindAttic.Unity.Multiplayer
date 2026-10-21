using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Unity.Multiplayer.Core;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

namespace Unity.Multiplayer.Testing
{
    /// <summary>
    /// A port nothing on this machine is using right now, for a test to serve on instead of a fixed one, so two test runs
    /// on one machine (parallel worktrees, another CI job, a local server) never collide.
    /// </summary>
    /// <remarks>
    /// Binds a socket to port 0, reads back the port the OS gave it and releases it. Another process could take the port
    /// between release and the test's own bind, but the OS hands out ephemeral ports round-robin, so in practice it doesn't.
    /// A <see cref="NetSession"/> takes the port through <see cref="NetConfig.Port"/>; a NetworkManager the NGO integration
    /// harness starts by itself takes it through <see cref="Assign(IEnumerable{NetworkManager})"/>.
    /// </remarks>
    public static class FreePort
    {
        /// <summary>A UDP port free right now: what <see cref="TransportKind.Udp"/> (Unity Transport's default) binds.</summary>
        public static ushort Udp()
        {
            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                socket.Bind(new IPEndPoint(IPAddress.Any, 0));
                return (ushort)((IPEndPoint)socket.LocalEndPoint).Port;
            }
        }

        /// <summary>A TCP port free right now: what <see cref="TransportKind.WebSocket"/> binds, or an HTTP service a test runs.</summary>
        public static ushort Tcp()
        {
            var listener = new TcpListener(IPAddress.Any, 0);
            listener.Start();
            try
            {
                return (ushort)((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        /// <summary><see cref="Tcp"/> for <see cref="TransportKind.WebSocket"/>, otherwise <see cref="Udp"/>.</summary>
        public static ushort For(TransportKind transport)
        {
            return transport == TransportKind.WebSocket ? Tcp() : Udp();
        }

        /// <summary>
        /// Points every NetworkManager's Unity Transport at one fresh <see cref="Udp"/> port, for managers the NGO integration
        /// harness starts by itself (no <see cref="NetSession"/>, so no <see cref="NetConfig"/> to carry the port). Returns
        /// the port, for a client the test adds later (<see cref="Assign(NetworkManager, ushort)"/>).
        /// </summary>
        public static ushort Assign(IEnumerable<NetworkManager> managers)
        {
            ushort port = Udp();
            foreach (NetworkManager manager in managers)
            {
                Assign(manager, port);
            }

            return port;
        }

        /// <summary>Points one NetworkManager's Unity Transport at <paramref name="port"/>.</summary>
        public static void Assign(NetworkManager manager, ushort port)
        {
            var transport = manager.NetworkConfig.NetworkTransport as UnityTransport;
            if (transport != null)
            {
                transport.ConnectionData.Port = port;
            }
        }
    }
}
