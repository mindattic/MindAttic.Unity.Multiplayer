using System.Collections;
using System.Linq;
using Unity.Collections;
using Unity.Multiplayer.Core;
using Unity.Multiplayer.Testing;
using Unity.Netcode;
using NUnit.Framework;
using UnityEngine;

namespace Unity.Multiplayer.Tests
{
    /// <summary>
    /// UM-T65: every client keeps sending to the server (reliable and unreliable named messages, every frame) while the
    /// server or host shuts down, and still ends with <see cref="DisconnectReason.ServerShutdown"/> rather than a transport
    /// failure; over WebSocket, where a send racing the server's close used to be misreported, and over UDP.
    /// </summary>
    [TestFixture(Topology.Server, TransportKind.WebSocket)]
    [TestFixture(Topology.Host, TransportKind.WebSocket)]
    [TestFixture(Topology.Server, TransportKind.Udp)]
    [TestFixture(Topology.Host, TransportKind.Udp)]
    public class ShutdownUnderLoadTests : NetSessionTest
    {
        private const string StreamMessage = "MindAttic.Multiplayer.Tests.Stream";

        public ShutdownUnderLoadTests(Topology topology, TransportKind transport) : base(topology, clients: 2, transport: transport)
        {
        }

        [UnityEngine.TestTools.UnityTest]
        public IEnumerator ServerShutsDown_WhileClientsStream_EveryClientSeesServerShutdown()
        {
            yield return StartAndJoinAll();

            int received = 0;
            Server.Manager.CustomMessagingManager.RegisterNamedMessageHandler(StreamMessage, (sender, reader) => received++);

            // Stream for a while first, so sends are in flight in both directions when the server goes.
            for (int frame = 0; frame < 30; frame++)
            {
                SendFromEveryClient(frame);
                yield return null;
            }

            Assert.That(received, Is.GreaterThan(0), "the server never got the clients' stream");

            Server.Leave();
            float giveUp = Time.realtimeSinceStartup + 15f;
            int sent = 30;
            while (!(Clients.All(c => c.State == SessionState.Disconnected) && Server.State == SessionState.Idle))
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(giveUp),
                    "the shutdown never finished (clients " + string.Join(", ", Clients.Select(c => c.State.ToString())) + ", server " + Server.State + ")");
                SendFromEveryClient(sent++);
                yield return null;
            }

            foreach (NetSession client in Clients)
            {
                Assert.That(client.LastDisconnect.Reason, Is.EqualTo(DisconnectReason.ServerShutdown), client.LastDisconnect.ToString());
            }

            Assert.That(Server.LastDisconnect.Reason, Is.EqualTo(DisconnectReason.LocalLeave));
        }

        /// <summary>A reliable and an unreliable message from each client still connected, as a game's input stream would be.</summary>
        private void SendFromEveryClient(int frame)
        {
            foreach (NetSession client in Clients)
            {
                NetworkManager manager = client.Manager;
                if (manager == null || !manager.IsConnectedClient || manager.ShutdownInProgress || manager.CustomMessagingManager == null)
                {
                    continue;
                }

                using (var writer = new FastBufferWriter(64, Allocator.Temp))
                {
                    writer.WriteValueSafe(frame);
                    writer.WriteValueSafe(Time.realtimeSinceStartupAsDouble);
                    manager.CustomMessagingManager.SendNamedMessage(StreamMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
                    manager.CustomMessagingManager.SendNamedMessage(StreamMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.Unreliable);
                }
            }
        }
    }
}
