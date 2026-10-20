using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MindAttic.Unity.Multiplayer.Core;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.TestHelpers.Runtime;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Utilities;
using UnityEngine;
using UnityEngine.TestTools;

namespace MindAttic.Unity.Multiplayer.Tests
{
    /// <summary>
    /// UM-T14: the session under bad network conditions. A Host and two Clients (Ann, Bob) over real Unity Transport on
    /// loopback, both clients on the <see cref="NetConditionsProfile.Lossy"/> profile (150 ms each way, ±10 ms jitter, 5%
    /// of packets dropped each way) through <see cref="NetConfig.Conditions"/>. Connecting, RPCs in every direction,
    /// synced fields and reconnecting to a held seat all still work: reliable traffic arrives, exactly once and in
    /// order, only later.
    /// </summary>
    public class LossyNetworkTests : NetcodeIntegrationTest
    {
        private const ushort Port = 47901;

        /// <summary>Many round trips at 300+ ms with resends: give every wait far more than the harness's 8 s default.</summary>
        private const float LossyTimeoutSeconds = 30f;

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

        public LossyNetworkTests() : base(HostOrServer.Host)
        {
        }

        protected override int NumberOfClients => 2;

        protected override bool CanStartServerAndClients() => false;

        protected override void OnServerAndClientsCreated()
        {
            NetSessionTestSupport.PrepareManagers(m_NetworkManagers);
            _host = new NetSession(m_ServerNetworkManager) { ProtocolHash = new ProtocolHash(0x14), Identity = new PlayerIdentity("host-token", "Host", PlayerColor.White) };
            _a = new NetSession(m_ClientNetworkManagers[0]) { ProtocolHash = new ProtocolHash(0x14), Identity = new PlayerIdentity("ann-token", "Ann", PlayerColor.White) };
            _b = new NetSession(m_ClientNetworkManagers[1]) { ProtocolHash = new ProtocolHash(0x14), Identity = new PlayerIdentity("bob-token", "Bob", PlayerColor.White) };
            _host.RpcRejected += r => _hostRejections.Add(r);
            _seats = new SeatEvents(_host);

            _prefab = new GameObject("TestAvatar");
            NetworkObject networkObject = _prefab.AddComponent<NetworkObject>();
            NetcodeIntegrationTestHelpers.MakeNetworkObjectTestPrefab(networkObject);
            _prefab.AddComponent<TestAvatar>();
            _host.Prefabs.Register(_prefab);
            _a.Prefabs.Register(_prefab);
            _b.Prefabs.Register(_prefab);

            base.OnServerAndClientsCreated();
        }

        protected override IEnumerator OnTearDown()
        {
            _a?.Dispose();
            _b?.Dispose();
            _host?.Dispose();
            _a = _b = _host = null;
            _hostRejections.Clear();
            yield return null;
        }

        private static NetConfig HostConfig()
        {
            return new NetConfig { Port = Port, GameVersion = "1", ReconnectGraceSeconds = 60 };
        }

        private static NetConfig LossyClientConfig()
        {
            return new NetConfig { Port = Port, GameVersion = "1", Conditions = NetConditionsProfile.Lossy };
        }

        private static TimeoutHelper Patient()
        {
            return new TimeoutHelper(LossyTimeoutSeconds);
        }

        private IEnumerator Wait(System.Func<bool> condition, string failure)
        {
            TimeoutHelper timeout = Patient();
            yield return WaitForConditionOrTimeOut(condition, timeout);
            AssertOnTimeout(failure, timeout);
        }

        private IEnumerator StartHost()
        {
            Assert.That(_host.StartHost(HostConfig()), Is.True, "StartHost");
            yield return Wait(() => _host.State == SessionState.Running, "the host never reached Running");
        }

        private IEnumerator JoinLossy(NetSession client)
        {
            Assert.That(client.JoinByAddress(NetConfig.LoopbackAddress, Port, LossyClientConfig()), Is.True, "JoinByAddress");
            yield return Wait(() => client.State == SessionState.Connected,
                "the client never connected over the lossy link (" + client.State + ", " + client.LastDisconnect + ")");
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

            GameObject spawned = SpawnObject(_prefab, m_ClientNetworkManagers[0]);
            _onHost = spawned.GetComponent<TestAvatar>();
            ulong id = _onHost.NetworkObjectId;
            yield return Wait(() => SeenBy(m_ClientNetworkManagers[0], id) && (!joinBob || SeenBy(m_ClientNetworkManagers[1], id)),
                "the clients never saw the avatar");
            _onA = Copy(m_ClientNetworkManagers[0], id);
            _onB = joinBob ? Copy(m_ClientNetworkManagers[1], id) : null;
        }

        private static bool SeenBy(NetworkManager manager, ulong id)
        {
            return manager.SpawnManager != null && manager.SpawnManager.SpawnedObjects.ContainsKey(id);
        }

        private static TestAvatar Copy(NetworkManager manager, ulong id)
        {
            return manager.SpawnManager.SpawnedObjects[id].GetComponent<TestAvatar>();
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
            yield return Wait(() =>
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

            yield return Wait(() => _onHost.Votes.Count >= 2 * calls, "not every vote arrived (" + _onHost.Votes.Count + " of " + (2 * calls) + ")");
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

            yield return Wait(() => _onA.Whistles.Count >= calls && _onB.Whistles.Count >= calls && _onA.Hints.Count >= calls && _onB.Tells.Count >= calls,
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

            yield return Wait(() => _onB.Tells.Count == 1, "the server's answer never came back");
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

            yield return Wait(() => _onA.Score == 30 && _onB.Score == 30, "Score never settled (Ann " + _onA.Score + ", Bob " + _onB.Score + ")");
            yield return Wait(() => Mathf.Approximately(_onA.Mass, 2.5f) && Mathf.Approximately(_onB.Mass, 2.5f),
                "Mass never replicated (Ann " + _onA.Mass + ", Bob " + _onB.Mass + ")");
            yield return Wait(() => _onA.MassChanges.Count == 1 && _onB.MassChanges.Count == 1, "OnChanged did not run on the clients");
            Assert.That(_onA.MassChanges[0], Is.EqualTo(new Vector2(0, 2.5f)), "previous and current value");
        }

        [UnityTest]
        public IEnumerator Synced_ALateJoinerOverTheLossyLinkGetsTheCurrentValue()
        {
            yield return StartWithAvatar(joinBob: false);
            _onHost.Score = 42;
            _onHost.Mass = 1.25f;

            yield return JoinLossy(_b);
            ulong id = _onHost.NetworkObjectId;
            yield return Wait(() => SeenBy(m_ClientNetworkManagers[1], id), "Bob never saw the avatar");
            TestAvatar onB = Copy(m_ClientNetworkManagers[1], id);

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
            yield return Wait(() => _seats.Held.Count == 1 && _a.CanStart, "the host never held the dropped player's seat");
            Assert.That(_seats.Held[0].Index, Is.EqualTo(seat.Index));

            yield return JoinLossy(_a);
            ulong secondId = _a.LocalClientId;
            Assert.That(secondId, Is.Not.EqualTo(firstId));
            Assert.That(_a.Conditions, Is.EqualTo(Lossy), "a new start applies the config's conditions to the new driver");
            yield return Wait(() => _seats.Reclaimed.Count == 1, "the host never gave the seat back");
            Assert.That(_seats.Reclaimed[0].Index, Is.EqualTo(seat.Index), "same seat");
            Assert.That(_host.GetSeatPlayerObject(seat.Index), Is.SameAs(playerObject), "same player object");
            Assert.That(playerObject.GetComponent<NetworkObject>().OwnerClientId, Is.EqualTo(secondId));

            NetworkManager client = m_ClientNetworkManagers[0];
            ulong objectId = playerObject.GetComponent<NetworkObject>().NetworkObjectId;
            yield return Wait(() => client.SpawnManager.SpawnedObjects.ContainsKey(objectId), "the client never saw its player object again");
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

            yield return Wait(() => _seats.Reclaimed.Count == 1 && _a.State == SessionState.Disconnected,
                "the new connection never took the seat over (old " + _a.State + ")");
            Assert.That(_seats.Reclaimed[0].ClientId, Is.EqualTo(newId));
            Assert.That(_host.GetSeatPlayerObject(seat.Index), Is.SameAs(playerObject), "same player object");

            // The stale connection is closed. Its reason (ReplacedMessage) is not asserted here: NGO sends it as a reliable
            // message and closes the transport connection at once, and the close is not delayed by the simulator stage, so
            // over this link the reason is lost and the client reports ServerShutdown. UM-T34 fixes that and asserts it here.
            Assert.That(_a.LastDisconnect.Reason, Is.EqualTo(DisconnectReason.Kicked).Or.EqualTo(DisconnectReason.ServerShutdown), _a.LastDisconnect.ToString());
            Assert.That(_b.State, Is.EqualTo(SessionState.Connected), "the new connection keeps the seat");
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
            Assert.That(_a.JoinByAddress(NetConfig.LoopbackAddress, Port, HostConfig()), Is.True);
            yield return Wait(() => _a.State == SessionState.Connected, "the client never connected");
            GameObject spawned = SpawnObject(_prefab, m_ClientNetworkManagers[0]);
            ulong id = spawned.GetComponent<NetworkObject>().NetworkObjectId;
            yield return Wait(() => SeenBy(m_ClientNetworkManagers[0], id), "Ann never saw the avatar");
            _onA = Copy(m_ClientNetworkManagers[0], id);
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
