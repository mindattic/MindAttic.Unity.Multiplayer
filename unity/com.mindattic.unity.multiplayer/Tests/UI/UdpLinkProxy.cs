using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Unity.Multiplayer.UI.Tests
{
    /// <summary>
    /// A UDP relay on loopback between clients and a server, with a switch that cuts the link: while
    /// <see cref="Blocked"/>, every packet both ways is dropped, transport heartbeats included, so the client's
    /// connection times out exactly as when Wi-Fi drops (the transport's network simulator cannot do this: it only
    /// delays and drops pipelined game traffic, never the connection's own keep-alives). Each client endpoint gets its own
    /// upstream socket, so the server sees separate connections.
    /// </summary>
    internal sealed class UdpLinkProxy : IDisposable
    {
        // Windows reports an ICMP "port unreachable" on the next receive of a UDP socket; this turns that off.
        private const int SioUdpConnReset = -1744830452;

        private readonly UdpClient _front;
        private readonly IPEndPoint _server;
        private readonly ConcurrentDictionary<IPEndPoint, UdpClient> _upstreams = new ConcurrentDictionary<IPEndPoint, UdpClient>();
        private volatile bool _stopped;

        public UdpLinkProxy(ushort serverPort)
        {
            _server = new IPEndPoint(IPAddress.Loopback, serverPort);
            _front = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            IgnoreConnReset(_front);
            Port = (ushort)((IPEndPoint)_front.Client.LocalEndPoint).Port;
            Task.Run(FrontLoop);
        }

        /// <summary>The port clients connect to.</summary>
        public ushort Port { get; }

        /// <summary>While true, nothing gets through in either direction.</summary>
        public volatile bool Blocked;

        private async Task FrontLoop()
        {
            while (!_stopped)
            {
                UdpReceiveResult received;
                try
                {
                    received = await _front.ReceiveAsync();
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (SocketException)
                {
                    continue;
                }

                if (Blocked)
                {
                    continue;
                }

                UdpClient upstream = _upstreams.GetOrAdd(received.RemoteEndPoint, OpenUpstream);
                try
                {
                    await upstream.SendAsync(received.Buffer, received.Buffer.Length);
                }
                catch (Exception)
                {
                    // The server may be gone between tests; drop the packet like a network would.
                }
            }
        }

        private UdpClient OpenUpstream(IPEndPoint client)
        {
            var upstream = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            IgnoreConnReset(upstream);
            upstream.Connect(_server);
            Task.Run(() => BackLoop(upstream, client));
            return upstream;
        }

        private async Task BackLoop(UdpClient upstream, IPEndPoint client)
        {
            while (!_stopped)
            {
                UdpReceiveResult received;
                try
                {
                    received = await upstream.ReceiveAsync();
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (SocketException)
                {
                    continue;
                }

                if (Blocked)
                {
                    continue;
                }

                try
                {
                    await _front.SendAsync(received.Buffer, received.Buffer.Length, client);
                }
                catch (Exception)
                {
                    // The client may be gone; drop the packet.
                }
            }
        }

        private static void IgnoreConnReset(UdpClient client)
        {
            try
            {
                client.Client.IOControl(SioUdpConnReset, new byte[] { 0, 0, 0, 0 }, null);
            }
            catch (Exception)
            {
                // Not Windows: nothing to turn off.
            }
        }

        public void Dispose()
        {
            _stopped = true;
            _front.Dispose();
            foreach (UdpClient upstream in _upstreams.Values)
            {
                upstream.Dispose();
            }
        }
    }
}
