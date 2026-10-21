using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Multiplayer.Core;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Multiplayer.Testing;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Utilities;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.Multiplayer.Tests
{
    /// <summary>
    /// UM-T14: the session under bad network conditions. A Host and two Clients (Ann, Bob) over real Unity Transport on
    /// loopback, both clients on the <see cref="NetConditionsProfile.Lossy"/> profile (150 ms each way, ±10 ms jitter, 5%
    /// of packets dropped each way) through <see cref="NetConfig.Conditions"/>. Connecting, RPCs in every direction,
    /// synced fields and reconnecting to a held seat all still work: reliable traffic arrives, exactly once and in
    /// order, only later.
    /// </summary>
    public class LossyNetworkTests : NetSessionTest
    {
        private static readonly NetConditions Lossy = NetConditions.For(NetConditionsProfile.Lossy);

        private NetSession _host;
        private NetSession _a;
        private NetSession _b;
        private GameObject _prefab;
        private SeatEvents _seats;
        private readonly List<RpcRejection> _hostRejections = new List<RpcRejection>();

        private TestAvatar _onHost;
        private TestAvatar _onA;
        private TestAvatar _onB;

        public LossyNetworkTests() : base(Topology.Host, clients: 2)
        {
        }

        /// <summary>Many round trips at 300+ ms with resends: give every wait far more than the usual 10 s.</summary>
        protected override float TimeoutSeconds => 30f;

        protected override string GameVersion => "1";

        protected override void OnSessionsCreated()
        {
            _host = Server;
            _a = Clients[0];
            _b = Clients[1];
            _host.ProtocolHash = _a.ProtocolHash = _b.ProtocolHash = new ProtocolHash(0x14);
            _host.Identity = new PlayerIdentity("host-token", "Host", PlayerColor.White);
            _a.Identity = new PlayerIdentity("ann-token", "Ann", PlayerColor.White);
            _b.Identity = new PlayerIdentity("bob-token", "Bob", PlayerColor.White);
            _host.RpcRejected += r => _hostRejections.Add(r);
            _seats = new SeatEvents(_host);
            _prefab = CreatePrefab<TestAvatar>("TestAvatar");
        }

        protected override IEnumerator OnSessionsTearDown()
        {
            _a = _b = _host = null;
            _hostRejections.Clear();
            yield return null;
        }

        private NetConfig HostConfig()
        {
            NetConfig config = Config();
            config.ReconnectGraceSeconds = 60;
            return config;
        }

        private NetConfig LossyClientConfig()
        {
            NetConfig config = Config();
            config.Conditions = NetConditionsProfile.Lossy;
            return config;
        }

        private IEnumerator StartHost()
        {
            yield return StartServer(HostConfig());
        }

        private IEnumerator JoinLossy(NetSession client)
        {
            yield return Join(client, LossyClientConfig());
        }

        /// <summary>Host up, Ann and Bob joined over the lossy link, one avatar owned by Ann seen everywhere.</summary>
        private IEnumerator StartWithAvatar(bool joinBob = true)
        {
            yield return StartHost();
            yield return JoinLossy(_a);
            if (joinBob)
            {
                yield return JoinLossy(_b);
            }

            _onHost = Spawn<TestAvatar>(_prefab, owner: _a);
            Awaiting<TestAvatar> onA = WaitForCopy(_a, _onHost);
            yield return onA;
            _onA = onA.Result;
            _onB = null;
            if (joinBob)
            {
                Awaiting<TestAvatar> onB = WaitForCopy(_b, _onHost);
                yield return onB;
                _onB = onB.Result;
            }
        }

        private static SimulatorUtility.Parameters SimulatorOf(NetworkManager manager)
        {
            var transport = (UnityTransport)manager.NetworkConfig.NetworkTransport;
            NetworkSettings settings = transport.GetNetworkDriver().CurrentSettings;
            return settings.GetSimulatorStageParameters();
        }

        private static ulong RttToServer(NetworkManager client)
        {
            return client.NetworkConfig.NetworkTransport.GetCurrentRtt(NetworkManager.ServerClientId);
        }

        /// <summary>
        /// Waits for Ann's measured round trip to the server to satisfy <paramref name="condition"/>. Unity Transport
        /// measures it from the acks of reliable packets, so Ann keeps sending reliable votes meanwhile.
        /// </summary>
        private IEnumerator WaitForAnnsRtt(System.Func<ulong, bool> condition, string failure)
        {
            int n = 0;
            yield return WaitUntil(() =>
            {
                _onA.Vote(n++);
                return condition(RttToServer(m_ClientNetworkManagers[0]));
            }, failure + " (round trip " + RttToServer(m_ClientNetworkManagers[0]) + " ms)");
        }

        // ---------------------------------------------------------------------------------------------------------
        // Connect
        // ---------------------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator Connect_OverTheLossyProfile_SeatsBothPlayers_WithTheSimulatorReallyOn()
        {
            yield return StartWithAvatar();

            Assert.That(_a.Conditions, Is.EqualTo(Lossy));
            Assert.That(_b.Conditions, Is.EqualTo(Lossy));
            Assert.That(_host.Conditions.IsNone, Is.True, "the host simulates nothing; the clients' links are the bad ones");

            SimulatorUtility.Parameters simulator = SimulatorOf(m_ClientNetworkManagers[0]);
            Assert.That(simulator.PacketDelayMs, Is.EqualTo(150));
            Assert.That(simulator.PacketJitterMs, Is.EqualTo(10));
            Assert.That(simulator.PacketDropPercentage, Is.EqualTo(5));
            Assert.That(simulator.Mode, Is.EqualTo(ApplyMode.AllPackets));
            Assert.That(SimulatorOf(m_ServerNetworkManager).PacketDelayMs, Is.Zero, "the host's own transport is untouched");

            Assert.That(_host.Seats.Select(s => s.DisplayName), Is.EquivalentTo(new[] { "Host", "Ann", "Bob" }));

            // 150 ms each way on the client's end alone: the transport measures at least ~280 ms of round trip.
            yield return WaitForAnnsRtt(rtt => rtt >= 250, "the measured round trip never showed the simulated latency");
            Assert.That(_a.State, Is.EqualTo(SessionState.Connected), "still connected after the round trips");
        }

        // ---------------------------------------------------------------------------------------------------------
        // RPCs
        // ---------------------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator ToServer_ReliableCalls_AllArriveOnceAndInOrder()
        {
            yield return StartWithAvatar();
            const int calls = 40;

            for (int i = 0; i < calls; i++)
            {
                _onA.Vote(i);
                _onB.Vote(1000 + i);
                if (i % 4 == 3)
                {
                    yield return null; // spread over several ticks and packets
                }
            }

            yield return WaitUntil(() => _onHost.Votes.Count >= 2 * calls, "not every vote arrived (" + _onHost.Votes.Count + " of " + (2 * calls) + ")");
            int[] fromAnn = _onHost.Votes.Where(v => v.Value.ClientId == _a.LocalClientId).Select(v => v.Key).ToArray();
            int[] fromBob = _onHost.Votes.Where(v => v.Value.ClientId == _b.LocalClientId).Select(v => v.Key).ToArray();
            Assert.That(fromAnn, Is.EqualTo(Enumerable.Range(0, calls).ToArray()), "Ann's votes: each once, in order");
            Assert.That(fromBob, Is.EqualTo(Enumerable.Range(1000, calls).ToArray()), "Bob's votes: each once, in order");
            Assert.That(_hostRejections, Is.Empty);
        }

        [UnityTest]
        public IEnumerator ToClients_ToOwner_ToClient_AllArriveOnceAndInOrder()
        {
            yield return StartWithAvatar();
            const int calls = 30;

            for (int i = 0; i < calls; i++)
            {
                _onHost.Whistle(new Color32((byte)i, 0, 0, 255));
                _onHost.Hint(i);
                _onHost.Tell(_b.LocalClientId, i);
                if (i % 3 == 2)
                {
                    yield return null;
                }
            }

            yield return WaitUntil(() => _onA.Whistles.Count >= calls && _onB.Whistles.Count >= calls && _onA.Hints.Count >= calls && _onB.Tells.Count >= calls,
                "not everything arrived (Ann whistles " + _onA.Whistles.Count + ", Bob whistles " + _onB.Whistles.Count
                + ", hints " + _onA.Hints.Count + ", tells " + _onB.Tells.Count + ")");

            int[] expected = Enumerable.Range(0, calls).ToArray();
            Assert.That(_onA.Whistles.Select(c => (int)c.r).ToArray(), Is.EqualTo(expected));
            Assert.That(_onB.Whistles.Select(c => (int)c.r).ToArray(), Is.EqualTo(expected));
            Assert.That(_onA.Hints, Is.EqualTo(expected), "[ToOwner] reached Ann, each once, in order");
            Assert.That(_onB.Tells, Is.EqualTo(expected), "[ToClient] reached Bob, each once, in order");
            Assert.That(_onB.Hints, Is.Empty, "Bob does not own the avatar");
            Assert.That(_onA.Tells, Is.Empty, "Ann was not the target");
        }

        [UnityTest]
        public IEnumerator ARoundTrip_ClientToServerToClient_Completes()
        {
            yield return StartWithAvatar();

            _onB.Vote(7); // OnVote answers with Tell(sender, 107)

            yield return WaitUntil(() => _onB.Tells.Count == 1, "the server's answer never came back");
            Assert.That(_onB.Tells[0], Is.EqualTo(107));
        }

        // ---------------------------------------------------------------------------------------------------------
        // [Synced]
        // ---------------------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator Synced_EveryClientEndsOnTheServersLastValue_AndOnChangedRuns()
        {
            yield return StartWithAvatar();

            for (int i = 1; i <= 30; i++)
            {
                _onHost.Score = i;
                yield return null;
            }

            _onHost.Mass = 2.5f;

            yield return WaitUntil(() => _onA.Score == 30 && _onB.Score == 30, "Score never settled (Ann " + _onA.Score + ", Bob " + _onB.Score + ")");
            yield return WaitUntil(() => Mathf.Approximately(_onA.Mass, 2.5f) && Mathf.Approximately(_onB.Mass, 2.5f),
                "Mass never replicated (Ann " + _onA.Mass + ", Bob " + _onB.Mass + ")");
            yield return WaitUntil(() => _onA.MassChanges.Count == 1 && _onB.MassChanges.Count == 1, "OnChanged did not run on the clients");
            Assert.That(_onA.MassChanges[0], Is.EqualTo(new Vector2(0, 2.5f)), "previous and current value");
        }

        [UnityTest]
        public IEnumerator Synced_ALateJoinerOverTheLossyLinkGetsTheCurrentValue()
        {
            yield return StartWithAvatar(joinBob: false);
            _onHost.Score = 42;
            _onHost.Mass = 1.25f;

            yield return JoinLossy(_b);
            Awaiting<TestAvatar> copy = WaitForCopy(_b, _onHost);
            yield return copy;
            TestAvatar onB = copy.Result;

            Assert.That(onB.Score, Is.EqualTo(42));
            Assert.That(onB.Mass, Is.EqualTo(1.25f));
        }

        // ---------------------------------------------------------------------------------------------------------
        // Reconnect
        // ---------------------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator DroppedClient_ReconnectingOverTheLossyLink_GetsTheSameSeatAndPlayerObject()
        {
            yield return StartHost();
            yield return JoinLossy(_a);
            ulong firstId = _a.LocalClientId;
            Assert.That(_host.TryGetSeat(firstId, out Seat seat), Is.True);
            GameObject playerObject = _host.GetSeatPlayerObject(seat.Index);
            Assert.That(playerObject, Is.Not.Null);

            _a.Leave();
            yield return WaitUntil(() => _seats.Held.Count == 1 && _a.CanStart, "the host never held the dropped player's seat");
            Assert.That(_seats.Held[0].Index, Is.EqualTo(seat.Index));

            yield return JoinLossy(_a);
            ulong secondId = _a.LocalClientId;
            Assert.That(secondId, Is.Not.EqualTo(firstId));
            Assert.That(_a.Conditions, Is.EqualTo(Lossy), "a new start applies the config's conditions to the new driver");
            yield return WaitUntil(() => _seats.Reclaimed.Count == 1, "the host never gave the seat back");
            Assert.That(_seats.Reclaimed[0].Index, Is.EqualTo(seat.Index), "same seat");
            Assert.That(_host.GetSeatPlayerObject(seat.Index), Is.SameAs(playerObject), "same player object");
            Assert.That(playerObject.GetComponent<NetworkObject>().OwnerClientId, Is.EqualTo(secondId));

            NetworkManager client = m_ClientNetworkManagers[0];
            ulong objectId = playerObject.GetComponent<NetworkObject>().NetworkObjectId;
            yield return WaitUntil(() => client.SpawnManager.SpawnedObjects.ContainsKey(objectId), "the client never saw its player object again");
            NetworkObject clientView = client.SpawnManager.SpawnedObjects[objectId];
            Assert.That(clientView.IsOwner && clientView.IsPlayerObject, Is.True);
        }

        [UnityTest]
        public IEnumerator KilledClient_ReconnectingOverTheLossyLink_TakesItsSeatBack()
        {
            yield return StartHost();
            yield return JoinLossy(_a);
            ulong deadId = _a.LocalClientId;
            Assert.That(_host.TryGetSeat(deadId, out Seat seat), Is.True);
            GameObject playerObject = _host.GetSeatPlayerObject(seat.Index);

            // Ann's game dies unnoticed and restarts with the same stored token: the second client manager.
            _b.Identity = _a.Identity;
            yield return JoinLossy(_b);
            ulong newId = _b.LocalClientId;

            yield return WaitUntil(() => _seats.Reclaimed.Count == 1 && _a.State == SessionState.Disconnected,
                "the new connection never took the seat over (old " + _a.State + ")");
            Assert.That(_seats.Reclaimed[0].ClientId, Is.EqualTo(newId));
            Assert.That(_host.GetSeatPlayerObject(seat.Index), Is.SameAs(playerObject), "same player object");

            // UM-T34: the stale connection is told why before it is closed. The reason goes first as its own reliable message
            // and the client leaves on it, so the delayed, lossy link cannot lose it to a close that overtakes it.
            Assert.That(_a.LastDisconnect.Reason, Is.EqualTo(DisconnectReason.Kicked), _a.LastDisconnect.ToString());
            Assert.That(_a.LastDisconnect.Message, Is.EqualTo(NetSession.ReplacedMessage));
            Assert.That(_b.State, Is.EqualTo(SessionState.Connected), "the new connection keeps the seat");
            Assert.That(playerObject.GetComponent<NetworkObject>().OwnerClientId, Is.EqualTo(newId), "the new connection owns the player object");

            // The stale connection's own close reaches the host and leaves the new connection's seat and player object alone.
            yield return WaitUntil(() => !m_ServerNetworkManager.ConnectedClients.ContainsKey(deadId), "the stale connection never closed");
            yield return null;
            Assert.That(_b.State, Is.EqualTo(SessionState.Connected), "still connected after the stale connection closed");
            Assert.That(playerObject.GetComponent<NetworkObject>().OwnerClientId, Is.EqualTo(newId), "still owns the player object");
            Assert.That(_host.TryGetSeat(newId, out Seat kept) && kept.Status == SeatStatus.Occupied, Is.True, "still seated");
        }

        [UnityTest]
        public IEnumerator Kick_OverTheLossyLink_TellsTheClientWhy()
        {
            yield return StartHost();
            yield return JoinLossy(_a);
            ulong id = _a.LocalClientId;

            Assert.That(_host.Kick(id, "Be nice."), Is.True);
            Assert.That(_host.Kick(id), Is.False, "already being removed");

            yield return WaitUntil(() => _a.State == SessionState.Disconnected, "the kicked client never left");
            Assert.That(_a.LastDisconnect.Reason, Is.EqualTo(DisconnectReason.Kicked), _a.LastDisconnect.ToString());
            Assert.That(_a.LastDisconnect.Message, Is.EqualTo("Be nice."));
            yield return WaitUntil(() => !m_ServerNetworkManager.ConnectedClients.ContainsKey(id), "the host never saw the kicked client go");
        }

        // ---------------------------------------------------------------------------------------------------------
        // Changing conditions at runtime
        // ---------------------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator SetConditions_DegradesALiveSession_AndCanTurnItOffAgain()
        {
            Assert.That(_a.SetConditions(Lossy), Is.False, "nothing is running yet");
            Assert.That(() => _a.SetConditions(new NetConditions(10, 50, 0)), Throws.ArgumentException, "jitter above latency");

            yield return StartHost();
            yield return Join(_a, HostConfig());
            Awaiting<TestAvatar> copy = WaitForCopy(_a, Spawn<TestAvatar>(_prefab, owner: _a));
            yield return copy;
            _onA = copy.Result;
            Assert.That(_a.Conditions.IsNone, Is.True);
            Assert.That(SimulatorOf(m_ClientNetworkManagers[0]).PacketDelayMs, Is.Zero);

            Assert.That(_a.SetConditions(Lossy), Is.True);
            Assert.That(_a.Conditions, Is.EqualTo(Lossy));
            Assert.That(SimulatorOf(m_ClientNetworkManagers[0]).PacketDropPercentage, Is.EqualTo(5));
            yield return WaitForAnnsRtt(rtt => rtt >= 250, "the live connection never slowed down");

            Assert.That(_a.SetConditions(NetConditions.None), Is.True);
            Assert.That(_a.Conditions.IsNone, Is.True);
            SimulatorUtility.Parameters off = SimulatorOf(m_ClientNetworkManagers[0]);
            Assert.That(off.PacketDelayMs + off.PacketJitterMs + off.PacketDropPercentage, Is.Zero);
            yield return WaitForAnnsRtt(rtt => rtt < 150, "the connection never sped up again");
        }

        /// <summary>Records the host's seat events.</summary>
        private sealed class SeatEvents
        {
            public readonly List<Seat> Held = new List<Seat>();
            public readonly List<Seat> Reclaimed = new List<Seat>();

            public SeatEvents(NetSession session)
            {
                session.SeatHeld += Held.Add;
                session.SeatReclaimed += Reclaimed.Add;
            }
        }
    }
}
