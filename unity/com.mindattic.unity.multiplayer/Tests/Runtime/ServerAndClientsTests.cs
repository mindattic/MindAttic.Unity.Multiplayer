using System.Collections;
using System.Linq;
using Unity.Multiplayer.Core;
using Unity.Multiplayer.Testing;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Unity.Multiplayer.Tests
{
    /// <summary>
    /// UM-T8, dedicated server: a Server with no local player and two Clients, all driven through
    /// <see cref="NetSession"/> over a real Unity Transport on loopback, over UDP and over WebSocket, on the test kit's
    /// <see cref="NetSessionTest"/> (UM-T56).
    /// </summary>
    [TestFixture(TransportKind.Udp)]
    [TestFixture(TransportKind.WebSocket)]
    public class ServerAndClientsTests : NetSessionTest
    {
        public ServerAndClientsTests(TransportKind transport) : base(Topology.Server, clients: 2, transport: transport)
        {
        }

        private string ClientStates()
        {
            return string.Join(", ", Clients.Select(c => c.State.ToString()));
        }

        [UnityTest]
        public IEnumerator ServerStarts_TwoClientsJoin_ThenLeaveOneByOne()
        {
            var serverProbe = new SessionProbe(Server);
            yield return StartAndJoinAll();

            Assert.That(Server.State, Is.EqualTo(SessionState.Running));
            Assert.That(Server.IsServer, Is.True);
            Assert.That(Server.IsHost, Is.False, "a dedicated server has no local player");
            Assert.That(Server.IsClient, Is.False);
            ulong first = Clients[0].LocalClientId;
            ulong second = Clients[1].LocalClientId;
            Assert.That(first, Is.Not.EqualTo(second));
            Assert.That(Server.ConnectedClientIds, Is.EquivalentTo(new[] { first, second }), "only the two remote clients");
            Assert.That(serverProbe.Joined, Is.EquivalentTo(new[] { first, second }));

            Clients[0].Leave();
            yield return WaitUntil(() => Server.ConnectedClientIds.Count == 1 && serverProbe.Left.Contains(first), "the server never saw the first client leave");
            Assert.That(Server.ConnectedClientIds, Is.EquivalentTo(new[] { second }));
            Assert.That(Clients[1].State, Is.EqualTo(SessionState.Connected), "the other client stays in");

            Clients[1].Leave();
            yield return WaitUntil(() => Server.ConnectedClientIds.Count == 0 && serverProbe.Left.Contains(second), "the server never saw the second client leave");
            Assert.That(Server.State, Is.EqualTo(SessionState.Running), "an empty dedicated server keeps running");

            Assert.That(Server.Leave(), Is.True);
            yield return WaitUntil(() => Server.State == SessionState.Idle, "the server never stopped");
            Assert.That(serverProbe.Ends.Single().Reason, Is.EqualTo(DisconnectReason.LocalLeave));
            Assert.That(Clients.All(c => c.LastDisconnect.Reason == DisconnectReason.LocalLeave), Is.True);
        }

        [UnityTest]
        public IEnumerator ServerStopping_DisconnectsEveryClient()
        {
            yield return StartAndJoinAll();

            Server.Leave();

            yield return WaitUntil(
                () => Clients.All(c => c.State == SessionState.Disconnected) && Server.State == SessionState.Idle,
                () => "the clients were not dropped when the server stopped (clients " + ClientStates() + ", server " + Server.State + ")");
            foreach (NetSession client in Clients)
            {
                Assert.That(client.LastDisconnect.Reason, Is.EqualTo(DisconnectReason.ServerShutdown), client.LastDisconnect.ToString());
                Assert.That(client.Clock, Is.Null);
            }

            Assert.That(Server.LastDisconnect.Reason, Is.EqualTo(DisconnectReason.LocalLeave));
        }

        [UnityTest]
        public IEnumerator ServerAndClientsTickAtTheConfiguredRate()
        {
            NetConfig config = Config();
            config.TickRate = 20;
            yield return StartAndJoinAll(config);

            Assert.That(Server.Clock.TickRate, Is.EqualTo(20));
            Assert.That(Clients.All(c => c.Clock.TickRate == 20), Is.True);

            long start = Server.Clock.CurrentTick;
            int ticked = 0;
            Server.Clock.Ticked += tick => ticked++;
            yield return WaitUntil(() => ticked >= 10, "the server clock did not tick");
            Assert.That(Server.Clock.CurrentTick - start, Is.GreaterThanOrEqualTo(ticked));
            Assert.That(Server.Clock.Time, Is.EqualTo(Server.Clock.CurrentTick * (1.0 / 20)).Within(1e-9));
        }
    }
}
