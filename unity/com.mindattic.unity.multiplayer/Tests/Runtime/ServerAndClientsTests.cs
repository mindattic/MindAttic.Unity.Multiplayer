using System.Collections;
using System.Linq;
using MindAttic.Unity.Multiplayer.Core;
using NUnit.Framework;
using Unity.Netcode.TestHelpers.Runtime;
using UnityEngine.TestTools;

namespace MindAttic.Unity.Multiplayer.Tests
{
    /// <summary>
    /// UM-T8, dedicated server: a Server with no local player and two Clients, all driven through
    /// <see cref="NetSession"/> over a real Unity Transport on loopback, over UDP and over WebSocket.
    /// </summary>
    [TestFixture(TransportKind.Udp, (ushort)47811)]
    [TestFixture(TransportKind.WebSocket, (ushort)47812)]
    public class ServerAndClientsTests : NetcodeIntegrationTest
    {
        private readonly TransportKind _transport;
        private readonly ushort _port;
        private NetSession _server;
        private NetSession[] _clients;

        public ServerAndClientsTests(TransportKind transport, ushort port) : base(HostOrServer.Server)
        {
            _transport = transport;
            _port = port;
        }

        protected override int NumberOfClients => 2;

        protected override bool CanStartServerAndClients() => false;

        protected override void OnServerAndClientsCreated()
        {
            NetSessionTestSupport.PrepareManagers(m_NetworkManagers);
            _server = new NetSession(m_ServerNetworkManager);
            _clients = m_ClientNetworkManagers.Select(manager => new NetSession(manager)).ToArray();
            base.OnServerAndClientsCreated();
        }

        protected override IEnumerator OnTearDown()
        {
            if (_clients != null)
            {
                foreach (NetSession client in _clients)
                {
                    client.Dispose();
                }
            }

            _server?.Dispose();
            _clients = null;
            _server = null;
            yield return null;
        }

        private NetConfig Config()
        {
            return new NetConfig { Port = _port, Transport = _transport, GameVersion = "test" };
        }

        private IEnumerator StartServerAndJoinBoth()
        {
            Assert.That(_server.StartServer(Config()), Is.True, "StartServer");
            foreach (NetSession client in _clients)
            {
                Assert.That(client.JoinByAddress(NetConfig.LoopbackAddress, _port, Config()), Is.True, "JoinByAddress");
            }

            yield return WaitForConditionOrTimeOut(() => _clients.All(c => c.State == SessionState.Connected) && _server.ConnectedClientIds.Count == 2);
            AssertOnTimeout("both clients never connected (clients " + string.Join(", ", _clients.Select(c => c.State.ToString()))
                + ", server sees " + _server.ConnectedClientIds.Count + ")");
        }

        [UnityTest]
        public IEnumerator ServerStarts_TwoClientsJoin_ThenLeaveOneByOne()
        {
            var serverProbe = new SessionProbe(_server);
            yield return StartServerAndJoinBoth();

            Assert.That(_server.State, Is.EqualTo(SessionState.Running));
            Assert.That(_server.IsServer, Is.True);
            Assert.That(_server.IsHost, Is.False, "a dedicated server has no local player");
            Assert.That(_server.IsClient, Is.False);
            ulong first = _clients[0].LocalClientId;
            ulong second = _clients[1].LocalClientId;
            Assert.That(first, Is.Not.EqualTo(second));
            Assert.That(_server.ConnectedClientIds, Is.EquivalentTo(new[] { first, second }), "only the two remote clients");
            Assert.That(serverProbe.Joined, Is.EquivalentTo(new[] { first, second }));

            _clients[0].Leave();
            yield return WaitForConditionOrTimeOut(() => _server.ConnectedClientIds.Count == 1 && serverProbe.Left.Contains(first));
            AssertOnTimeout("the server never saw the first client leave");
            Assert.That(_server.ConnectedClientIds, Is.EquivalentTo(new[] { second }));
            Assert.That(_clients[1].State, Is.EqualTo(SessionState.Connected), "the other client stays in");

            _clients[1].Leave();
            yield return WaitForConditionOrTimeOut(() => _server.ConnectedClientIds.Count == 0 && serverProbe.Left.Contains(second));
            AssertOnTimeout("the server never saw the second client leave");
            Assert.That(_server.State, Is.EqualTo(SessionState.Running), "an empty dedicated server keeps running");

            Assert.That(_server.Leave(), Is.True);
            yield return WaitForConditionOrTimeOut(() => _server.State == SessionState.Idle);
            AssertOnTimeout("the server never stopped");
            Assert.That(serverProbe.Ends.Single().Reason, Is.EqualTo(DisconnectReason.LocalLeave));
            Assert.That(_clients.All(c => c.LastDisconnect.Reason == DisconnectReason.LocalLeave), Is.True);
        }

        [UnityTest]
        public IEnumerator ServerStopping_DisconnectsEveryClient()
        {
            yield return StartServerAndJoinBoth();

            _server.Leave();

            yield return WaitForConditionOrTimeOut(() => _clients.All(c => c.State == SessionState.Disconnected) && _server.State == SessionState.Idle);
            AssertOnTimeout("the clients were not dropped when the server stopped (clients "
                + string.Join(", ", _clients.Select(c => c.State.ToString())) + ", server " + _server.State + ")");
            foreach (NetSession client in _clients)
            {
                Assert.That(client.LastDisconnect.Reason, Is.EqualTo(DisconnectReason.ServerShutdown), client.LastDisconnect.ToString());
                Assert.That(client.Clock, Is.Null);
            }

            Assert.That(_server.LastDisconnect.Reason, Is.EqualTo(DisconnectReason.LocalLeave));
        }

        [UnityTest]
        public IEnumerator ServerAndClientsTickAtTheConfiguredRate()
        {
            NetConfig config = Config();
            config.TickRate = 20;
            Assert.That(_server.StartServer(config), Is.True);
            foreach (NetSession client in _clients)
            {
                Assert.That(client.JoinByAddress(NetConfig.LoopbackAddress, _port, config), Is.True);
            }

            yield return WaitForConditionOrTimeOut(() => _clients.All(c => c.State == SessionState.Connected));
            AssertOnTimeout("the clients never connected");

            Assert.That(_server.Clock.TickRate, Is.EqualTo(20));
            Assert.That(_clients.All(c => c.Clock.TickRate == 20), Is.True);

            long start = _server.Clock.CurrentTick;
            int ticked = 0;
            _server.Clock.Ticked += tick => ticked++;
            yield return WaitForConditionOrTimeOut(() => ticked >= 10);
            AssertOnTimeout("the server clock did not tick");
            Assert.That(_server.Clock.CurrentTick - start, Is.GreaterThanOrEqualTo(ticked));
            Assert.That(_server.Clock.Time, Is.EqualTo(_server.Clock.CurrentTick * (1.0 / 20)).Within(1e-9));
        }
    }
}
