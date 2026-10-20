using System.Collections;
using System.Linq;
using MindAttic.Unity.Multiplayer.Core;
using NUnit.Framework;
using Unity.Netcode.TestHelpers.Runtime;
using UnityEngine;
using UnityEngine.TestTools;

namespace MindAttic.Unity.Multiplayer.Tests
{
    /// <summary>
    /// UM-T8, listen server: a Host and one Client, both driven only through <see cref="NetSession"/> over a
    /// real Unity Transport on loopback, once over UDP and once over WebSocket. NetcodeIntegrationTest creates
    /// and destroys the NetworkManagers; NetSession starts and stops them.
    /// </summary>
    [TestFixture(TransportKind.Udp, (ushort)47801)]
    [TestFixture(TransportKind.WebSocket, (ushort)47802)]
    public class HostAndClientTests : NetcodeIntegrationTest
    {
        private readonly TransportKind _transport;
        private readonly ushort _port;
        private NetSession _host;
        private NetSession _client;

        public HostAndClientTests(TransportKind transport, ushort port) : base(HostOrServer.Host)
        {
            _transport = transport;
            _port = port;
        }

        protected override int NumberOfClients => 1;

        // NetSession starts the managers, not the test harness.
        protected override bool CanStartServerAndClients() => false;

        protected override void OnServerAndClientsCreated()
        {
            NetSessionTestSupport.PrepareManagers(m_NetworkManagers);
            _host = new NetSession(m_ServerNetworkManager);
            _client = new NetSession(m_ClientNetworkManagers[0]);
            base.OnServerAndClientsCreated();
        }

        protected override IEnumerator OnTearDown()
        {
            _client?.Dispose();
            _host?.Dispose();
            _client = null;
            _host = null;
            yield return null;
        }

        private NetConfig Config()
        {
            return new NetConfig { Port = _port, Transport = _transport, GameVersion = "test" };
        }

        [UnityTest]
        public IEnumerator HostStarts_ClientJoinsByAddress_ThenBothLeave()
        {
            var hostProbe = new SessionProbe(_host);
            var clientProbe = new SessionProbe(_client);

            Assert.That(_host.StartHost(Config()), Is.True, "StartHost");
            yield return WaitForConditionOrTimeOut(() => _host.State == SessionState.Running);
            AssertOnTimeout("the host never reached Running (state " + _host.State + ")");
            Assert.That(_host.IsServer && _host.IsHost && _host.IsClient, Is.True);
            Assert.That(_host.Topology, Is.EqualTo(Topology.Host));
            Assert.That(hostProbe.Starts, Is.EqualTo(1));
            Assert.That(_host.Clock, Is.Not.Null, "a running session has a clock");
            Assert.That(_host.Clock.TickRate, Is.EqualTo(NetConfig.DefaultTickRate));

            Assert.That(_client.JoinByAddress(NetConfig.LoopbackAddress, _port, Config()), Is.True, "JoinByAddress");
            Assert.That(_client.State, Is.EqualTo(SessionState.Connecting));

            yield return WaitForConditionOrTimeOut(() => _client.State == SessionState.Connected && _host.ConnectedClientIds.Count == 2);
            AssertOnTimeout("the client never connected (client " + _client.State + ", host sees " + _host.ConnectedClientIds.Count + " clients)");
            ulong clientId = _client.LocalClientId;
            Assert.That(clientId, Is.Not.EqualTo(0UL));
            Assert.That(_client.IsClient, Is.True);
            Assert.That(_client.IsServer, Is.False);
            Assert.That(_client.ConnectedClientIds, Is.Empty, "a client does not see the server's client list");
            Assert.That(hostProbe.Joined, Does.Contain(clientId));
            Assert.That(clientProbe.Starts, Is.EqualTo(1));
            Assert.That(_client.Config.Transport, Is.EqualTo(_transport));

            // Both clocks tick at the session's rate.
            long hostTick = _host.Clock.CurrentTick;
            long clientTick = _client.Clock.CurrentTick;
            yield return WaitForConditionOrTimeOut(() => _host.Clock.CurrentTick >= hostTick + 5 && _client.Clock.CurrentTick >= clientTick + 5);
            AssertOnTimeout("the clocks did not tick");

            Assert.That(_client.Leave(), Is.True);
            Assert.That(_client.State, Is.EqualTo(SessionState.Disconnected));
            Assert.That(_client.LastDisconnect.Reason, Is.EqualTo(DisconnectReason.LocalLeave));
            Assert.That(_client.Clock, Is.Null, "an ended session has no clock");
            yield return WaitForConditionOrTimeOut(() => hostProbe.Left.Contains(clientId) && _host.ConnectedClientIds.Count == 1);
            AssertOnTimeout("the host never saw the client leave");
            Assert.That(_host.State, Is.EqualTo(SessionState.Running), "a client leaving does not end the host");

            Assert.That(_host.Leave(), Is.True);
            yield return WaitForConditionOrTimeOut(() => _host.State == SessionState.Idle);
            AssertOnTimeout("the host never stopped (state " + _host.State + ")");
            Assert.That(_host.LastDisconnect.Reason, Is.EqualTo(DisconnectReason.LocalLeave));
            Assert.That(hostProbe.Path(), Is.EqualTo(new[] { SessionState.Idle, SessionState.Starting, SessionState.Running, SessionState.Stopping, SessionState.Idle }));
            Assert.That(hostProbe.Ends.Single().Reason, Is.EqualTo(DisconnectReason.LocalLeave));
            Assert.That(clientProbe.Path(), Is.EqualTo(new[] { SessionState.Idle, SessionState.Connecting, SessionState.Connected, SessionState.Disconnected }));
            Assert.That(clientProbe.Ends.Single().Reason, Is.EqualTo(DisconnectReason.LocalLeave));
            Assert.That(_host.Leave(), Is.False, "nothing left to leave");
        }

        [UnityTest]
        public IEnumerator HostLeaving_DisconnectsTheClient()
        {
            var clientProbe = new SessionProbe(_client);
            Assert.That(_host.StartHost(Config()), Is.True);
            Assert.That(_client.JoinByAddress(NetConfig.LoopbackAddress, _port, Config()), Is.True);
            yield return WaitForConditionOrTimeOut(() => _client.State == SessionState.Connected);
            AssertOnTimeout("the client never connected");

            _host.Leave();

            yield return WaitForConditionOrTimeOut(() => _client.State == SessionState.Disconnected && _host.State == SessionState.Idle);
            AssertOnTimeout("the client was not dropped when the host left (client " + _client.State + ", host " + _host.State + ")");
            Assert.That(_client.LastDisconnect.Reason, Is.EqualTo(DisconnectReason.ServerShutdown), _client.LastDisconnect.ToString());
            Assert.That(clientProbe.Ends, Has.Count.EqualTo(1));
            Assert.That(_client.Clock, Is.Null);
        }

        [UnityTest]
        public IEnumerator ClientCanJoinAgainAfterLeaving()
        {
            Assert.That(_host.StartHost(Config()), Is.True);
            Assert.That(_client.JoinByAddress(NetConfig.LoopbackAddress, _port, Config()), Is.True);
            yield return WaitForConditionOrTimeOut(() => _client.State == SessionState.Connected);
            AssertOnTimeout("the first join never connected");

            _client.Leave();
            yield return WaitForConditionOrTimeOut(() => _client.CanStart && _host.ConnectedClientIds.Count == 1);
            AssertOnTimeout("the client never finished shutting down");

            Assert.That(_client.JoinByAddress(NetConfig.LoopbackAddress, _port, Config()), Is.True, "second JoinByAddress");
            yield return WaitForConditionOrTimeOut(() => _client.State == SessionState.Connected && _host.ConnectedClientIds.Count == 2);
            AssertOnTimeout("the second join never connected");
        }

        [UnityTest]
        public IEnumerator JoinWithNoServer_EndsWithConnectFailed()
        {
            LogAssert.Expect(LogType.Error, "Failed to connect to server."); // Unity Transport's own report
            Assert.That(_client.JoinByAddress(NetConfig.LoopbackAddress, (ushort)(_port + 50), Config()), Is.True);
            Assert.That(_client.State, Is.EqualTo(SessionState.Connecting));

            // The harness's transport makes 4 attempts of 500 ms each.
            var timeout = new TimeoutHelper(10f);
            yield return WaitForConditionOrTimeOut(() => _client.State == SessionState.Disconnected, timeout);
            AssertOnTimeout("the failed join never ended (state " + _client.State + ")", timeout);
            Assert.That(_client.LastDisconnect.Reason, Is.EqualTo(DisconnectReason.ConnectFailed));
        }

        [UnityTest]
        public IEnumerator StartingTwice_Throws()
        {
            Assert.That(_host.StartHost(Config()), Is.True);
            Assert.Throws<System.InvalidOperationException>(() => _host.StartServer(Config()));
            yield return WaitForConditionOrTimeOut(() => _host.State == SessionState.Running);
            AssertOnTimeout("the host never reached Running");
        }

        [UnityTest]
        public IEnumerator InvalidConfig_ThrowsAndChangesNothing()
        {
            Assert.Throws<System.ArgumentException>(() => _host.StartHost(new NetConfig { Port = 0 }));
            Assert.Throws<System.ArgumentException>(() => _client.JoinByAddress("0.0.0.0", _port, Config()));
            Assert.That(_host.State, Is.EqualTo(SessionState.Idle));
            Assert.That(_client.State, Is.EqualTo(SessionState.Idle));
            yield return null;
        }
    }
}
