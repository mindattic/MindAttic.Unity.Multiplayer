using System.Collections;
using System.Linq;
using Unity.Multiplayer.Core;
using Unity.Multiplayer.Testing;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.Multiplayer.Tests
{
    /// <summary>
    /// UM-T8, listen server: a Host and one Client, both driven only through <see cref="NetSession"/> over a
    /// real Unity Transport on loopback, once over UDP and once over WebSocket. The test kit's
    /// <see cref="NetSessionTest"/> (UM-T56) makes the sessions on a free port; the tests start and stop them by hand.
    /// </summary>
    [TestFixture(TransportKind.Udp)]
    [TestFixture(TransportKind.WebSocket)]
    public class HostAndClientTests : NetSessionTest
    {
        public HostAndClientTests(TransportKind transport) : base(Topology.Host, clients: 1, transport: transport)
        {
        }

        [UnityTest]
        public IEnumerator HostStarts_ClientJoinsByAddress_ThenBothLeave()
        {
            NetSession host = Server;
            var hostProbe = new SessionProbe(host);
            var clientProbe = new SessionProbe(Client);

            Assert.That(host.StartHost(Config()), Is.True, "StartHost");
            yield return WaitUntil(() => host.State == SessionState.Running, () => "the host never reached Running (state " + host.State + ")");
            Assert.That(host.IsServer && host.IsHost && host.IsClient, Is.True);
            Assert.That(host.Topology, Is.EqualTo(Topology.Host));
            Assert.That(hostProbe.Starts, Is.EqualTo(1));
            Assert.That(host.Clock, Is.Not.Null, "a running session has a clock");
            Assert.That(host.Clock.TickRate, Is.EqualTo(NetConfig.DefaultTickRate));

            Assert.That(Client.JoinByAddress(NetConfig.LoopbackAddress, Port, Config()), Is.True, "JoinByAddress");
            Assert.That(Client.State, Is.EqualTo(SessionState.Connecting));

            yield return WaitUntil(
                () => Client.State == SessionState.Connected && host.ConnectedClientIds.Count == 2,
                () => "the client never connected (client " + Client.State + ", host sees " + host.ConnectedClientIds.Count + " clients)");
            ulong clientId = Client.LocalClientId;
            Assert.That(clientId, Is.Not.EqualTo(0UL));
            Assert.That(Client.IsClient, Is.True);
            Assert.That(Client.IsServer, Is.False);
            Assert.That(Client.ConnectedClientIds, Is.Empty, "a client does not see the server's client list");
            Assert.That(hostProbe.Joined, Does.Contain(clientId));
            Assert.That(clientProbe.Starts, Is.EqualTo(1));
            Assert.That(Client.Config.Transport, Is.EqualTo(Transport));

            // Both clocks tick at the session's rate.
            long hostTick = host.Clock.CurrentTick;
            long clientTick = Client.Clock.CurrentTick;
            yield return WaitUntil(() => host.Clock.CurrentTick >= hostTick + 5 && Client.Clock.CurrentTick >= clientTick + 5, "the clocks did not tick");

            Assert.That(Client.Leave(), Is.True);
            Assert.That(Client.State, Is.EqualTo(SessionState.Disconnected));
            Assert.That(Client.LastDisconnect.Reason, Is.EqualTo(DisconnectReason.LocalLeave));
            Assert.That(Client.Clock, Is.Null, "an ended session has no clock");
            yield return WaitUntil(() => hostProbe.Left.Contains(clientId) && host.ConnectedClientIds.Count == 1, "the host never saw the client leave");
            Assert.That(host.State, Is.EqualTo(SessionState.Running), "a client leaving does not end the host");

            Assert.That(host.Leave(), Is.True);
            yield return WaitUntil(() => host.State == SessionState.Idle, () => "the host never stopped (state " + host.State + ")");
            Assert.That(host.LastDisconnect.Reason, Is.EqualTo(DisconnectReason.LocalLeave));
            Assert.That(hostProbe.Path(), Is.EqualTo(new[] { SessionState.Idle, SessionState.Starting, SessionState.Running, SessionState.Stopping, SessionState.Idle }));
            Assert.That(hostProbe.Ends.Single().Reason, Is.EqualTo(DisconnectReason.LocalLeave));
            Assert.That(clientProbe.Path(), Is.EqualTo(new[] { SessionState.Idle, SessionState.Connecting, SessionState.Connected, SessionState.Disconnected }));
            Assert.That(clientProbe.Ends.Single().Reason, Is.EqualTo(DisconnectReason.LocalLeave));
            Assert.That(host.Leave(), Is.False, "nothing left to leave");
        }

        [UnityTest]
        public IEnumerator HostLeaving_DisconnectsTheClient()
        {
            var clientProbe = new SessionProbe(Client);
            yield return StartAndJoinAll();

            Server.Leave();

            yield return WaitUntil(
                () => Client.State == SessionState.Disconnected && Server.State == SessionState.Idle,
                () => "the client was not dropped when the host left (client " + Client.State + ", host " + Server.State + ")");
            Assert.That(Client.LastDisconnect.Reason, Is.EqualTo(DisconnectReason.ServerShutdown), Client.LastDisconnect.ToString());
            Assert.That(clientProbe.Ends, Has.Count.EqualTo(1));
            Assert.That(Client.Clock, Is.Null);
        }

        [UnityTest]
        public IEnumerator ClientCanJoinAgainAfterLeaving()
        {
            yield return StartAndJoinAll();

            Client.Leave();
            yield return WaitUntil(() => Client.CanStart && Server.ConnectedClientIds.Count == 1, "the client never finished shutting down");

            yield return Join(Client);
            yield return WaitUntil(() => Server.ConnectedClientIds.Count == 2, "the host never saw the second join");
        }

        [UnityTest]
        public IEnumerator JoinWithNoServer_EndsWithConnectFailed()
        {
            LogAssert.Expect(LogType.Error, "Failed to connect to server."); // Unity Transport's own report
            Assert.That(Client.JoinByAddress(NetConfig.LoopbackAddress, FreePort.For(Transport), Config()), Is.True);
            Assert.That(Client.State, Is.EqualTo(SessionState.Connecting));

            // The harness's transport makes 4 attempts of 500 ms each.
            yield return WaitUntil(() => Client.State == SessionState.Disconnected, () => "the failed join never ended (state " + Client.State + ")");
            Assert.That(Client.LastDisconnect.Reason, Is.EqualTo(DisconnectReason.ConnectFailed));
        }

        [UnityTest]
        public IEnumerator StartingTwice_Throws()
        {
            Assert.That(Server.StartHost(Config()), Is.True);
            Assert.Throws<System.InvalidOperationException>(() => Server.StartServer(Config()));
            yield return WaitUntil(() => Server.State == SessionState.Running, "the host never reached Running");
        }

        [UnityTest]
        public IEnumerator InvalidConfig_ThrowsAndChangesNothing()
        {
            Assert.Throws<System.ArgumentException>(() => Server.StartHost(new NetConfig { Port = 0 }));
            Assert.Throws<System.ArgumentException>(() => Client.JoinByAddress("0.0.0.0", Port, Config()));
            Assert.That(Server.State, Is.EqualTo(SessionState.Idle));
            Assert.That(Client.State, Is.EqualTo(SessionState.Idle));
            yield return null;
        }
    }
}
