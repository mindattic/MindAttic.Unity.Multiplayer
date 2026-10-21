using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Multiplayer.Core;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Multiplayer.Testing;
using Unity.Netcode.TestHelpers.Runtime;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.Multiplayer.Tests
{
    /// <summary>
    /// UM-T10: NetEntity RPCs in every direction and synced fields, over a real Unity Transport on loopback. A Host and
    /// two Clients (Ann, Bob) driven through <see cref="NetSession"/>; one <see cref="TestAvatar"/> owned by Ann. The
    /// avatar's plumbing is the source generator's output (UM-T11), which replaced UM-T10's hand-written stand-in with
    /// these tests unchanged.
    /// </summary>
    public class EntityRpcTests : NetSessionTest
    {
        private NetSession _host;
        private NetSession _a;
        private NetSession _b;
        private GameObject _prefab;
        private readonly List<RpcRejection> _hostRejections = new List<RpcRejection>();
        private readonly List<RpcRejection> _aRejections = new List<RpcRejection>();

        private TestAvatar _onHost;
        private TestAvatar _onA;
        private TestAvatar _onB;

        public EntityRpcTests() : base(Topology.Host, clients: 2)
        {
        }

        protected override string GameVersion => "1";

        protected override void OnSessionsCreated()
        {
            _host = Server;
            _a = Clients[0];
            _b = Clients[1];
            _host.ProtocolHash = _a.ProtocolHash = _b.ProtocolHash = new ProtocolHash(0x10);
            _host.Identity = new PlayerIdentity("host-token", "Host", PlayerColor.White);
            _a.Identity = new PlayerIdentity("ann-token", "Ann", PlayerColor.White);
            _b.Identity = new PlayerIdentity("bob-token", "Bob", PlayerColor.White);
            _host.RpcRejected += r => _hostRejections.Add(r);
            _a.RpcRejected += r => _aRejections.Add(r);

            // The prefab is registered through the sessions' registries (not the harness), on every peer, before start.
            _prefab = new GameObject("TestAvatar");
            NetworkObject networkObject = _prefab.AddComponent<NetworkObject>();
            NetcodeIntegrationTestHelpers.MakeNetworkObjectTestPrefab(networkObject);
            _prefab.AddComponent<TestAvatar>();
            Assert.That(_host.Prefabs.Register(_prefab), Is.True);
            Assert.That(_a.Prefabs.Register(_prefab), Is.True);
            Assert.That(_b.Prefabs.Register(_prefab), Is.True);
            Assert.That(_host.Prefabs.Register(_prefab), Is.False, "registering twice is harmless");
            Assert.That(_host.Prefabs.Contains(_prefab), Is.True);
        }

        protected override IEnumerator OnSessionsTearDown()
        {
            _a = _b = _host = null;
            _hostRejections.Clear();
            _aRejections.Clear();
            yield return null;
        }

        /// <summary>Host up, Ann and (unless told otherwise) Bob joined, one avatar owned by Ann seen everywhere.</summary>
        private IEnumerator StartWithAvatar(bool joinBob = true)
        {
            Assert.That(_host.StartHost(Config()), Is.True, "StartHost");
            yield return WaitUntil(() => _host.State == SessionState.Running, "the host never reached Running");
            yield return Join(_a);
            if (joinBob)
            {
                yield return Join(_b);
            }

            GameObject spawned = SpawnObject(_prefab, m_ClientNetworkManagers[0]);
            _onHost = spawned.GetComponent<TestAvatar>();
            ulong id = _onHost.NetworkObjectId;
            yield return WaitUntil(() => SeenBy(m_ClientNetworkManagers[0], id) && (!joinBob || SeenBy(m_ClientNetworkManagers[1], id)), "the clients never saw the avatar");
            _onA = Copy(m_ClientNetworkManagers[0], id);
            _onB = joinBob ? Copy(m_ClientNetworkManagers[1], id) : null;
            Assert.That(_onA.IsOwner, Is.True, "Ann owns the avatar");
            Assert.That(_onA.Spawns, Is.EqualTo(1), "OnSpawned ran on Ann's copy");
        }

        private static bool SeenBy(NetworkManager manager, ulong id)
        {
            return manager.SpawnManager != null && manager.SpawnManager.SpawnedObjects.ContainsKey(id);
        }

        private static TestAvatar Copy(NetworkManager manager, ulong id)
        {
            return manager.SpawnManager.SpawnedObjects[id].GetComponent<TestAvatar>();
        }

        /// <summary>Lets a few network ticks pass, for asserting that something did NOT arrive.</summary>
        private static IEnumerator Settle()
        {
            for (int i = 0; i < 20; i++)
            {
                yield return null;
            }
        }

        // ---------------------------------------------------------------------------------------------------------
        // [ToServer]
        // ---------------------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator ToServer_RunsOnTheServer_WithTheSendersIdSeatAndName()
        {
            yield return StartWithAvatar();

            _onA.Move(new Vector2(3, 4));

            yield return WaitUntil(() => _onHost.Moves.Count == 1, "Move never reached the server");
            NetSender sender = _onHost.Moves[0].Value;
            Assert.That(_onHost.Moves[0].Key, Is.EqualTo(new Vector2(3, 4)));
            Assert.That(sender.ClientId, Is.EqualTo(_a.LocalClientId));
            Seat annSeat;
            Assert.That(_host.TryGetSeat(_a.LocalClientId, out annSeat), Is.True);
            Assert.That(sender.SeatIndex, Is.EqualTo(annSeat.Index));
            Assert.That(sender.DisplayName, Is.EqualTo("Ann"));
            Assert.That(_onHost.Owner, Is.EqualTo(sender), "NetEntity.Owner resolves the same way");

            yield return Settle();
            Assert.That(_onA.Moves, Is.Empty, "handlers of [ToServer] run only on the server");
            Assert.That(_onB.Moves, Is.Empty);
            Assert.That(_hostRejections, Is.Empty);
        }

        [UnityTest]
        public IEnumerator ToServer_FromANonOwner_IsDroppedAndReported()
        {
            yield return StartWithAvatar();

            _onB.Move(new Vector2(9, 9));

            yield return WaitUntil(() => _hostRejections.Count == 1, "the server never refused Bob's Move");
            Assert.That(_hostRejections[0].Verdict, Is.EqualTo(RpcVerdict.NotOwner));
            Assert.That(_hostRejections[0].SenderClientId, Is.EqualTo(_b.LocalClientId));
            Assert.That(_hostRejections[0].Rpc, Does.EndWith("TestAvatar.Move"));
            Assert.That(_hostRejections[0].ObjectId, Is.EqualTo(_onHost.NetworkObjectId));
            Assert.That(_onHost.Moves, Is.Empty, "a refused call's handler never runs");

            _onA.Move(new Vector2(1, 1));
            yield return WaitUntil(() => _onHost.Moves.Count == 1, "the owner's Move was not accepted after a non-owner's was refused");
        }

        [UnityTest]
        public IEnumerator ToServer_WithoutOwnership_AcceptsAnyClient_AndTheHostItself()
        {
            yield return StartWithAvatar();

            _onB.Vote(2);
            _onHost.Vote(1);

            yield return WaitUntil(() => _onHost.Votes.Count == 2, () => "both votes should reach the server (got " + _onHost.Votes.Count + ")");
            NetSender bob = _onHost.Votes.Single(v => v.Key == 2).Value;
            NetSender host = _onHost.Votes.Single(v => v.Key == 1).Value;
            Assert.That(bob.ClientId, Is.EqualTo(_b.LocalClientId));
            Assert.That(bob.DisplayName, Is.EqualTo("Bob"));
            Assert.That(host.IsServer, Is.True, "a host calling a [ToServer] method is the sender itself");
            Assert.That(host.DisplayName, Is.EqualTo("Host"));
            Assert.That(host.HasSeat, Is.True, "the host's own player is seated");
        }

        [UnityTest]
        public IEnumerator ToServer_OverItsRateLimit_IsThrottledPerSender()
        {
            yield return StartWithAvatar();
            const int flood = 40;

            for (int i = 0; i < flood; i++)
            {
                _onA.Ping(i);
            }

            for (int i = 0; i < 3; i++)
            {
                _onB.Ping(1000 + i);
            }

            yield return WaitForConditionOrTimeOut(() => _hostRejections.Count + _onHost.Pings.Count(p => p.Value.ClientId == _a.LocalClientId) == flood
                && _onHost.Pings.Count(p => p.Value.ClientId == _b.LocalClientId) == 3);
            AssertOnTimeout("not every ping was accounted for (accepted " + _onHost.Pings.Count + ", refused " + _hostRejections.Count + ")");

            int annAccepted = _onHost.Pings.Count(p => p.Value.ClientId == _a.LocalClientId);
            // The bucket holds one second's worth (5) and refills at 5/s; the flood arrives within a frame or two.
            Assert.That(annAccepted, Is.InRange(TestAvatar.PingRateLimit, TestAvatar.PingRateLimit + 2), "Ann's accepted pings");
            Assert.That(_onHost.Pings.Where(p => p.Value.ClientId == _a.LocalClientId).Select(p => p.Key).Take(TestAvatar.PingRateLimit),
                Is.EqualTo(Enumerable.Range(0, TestAvatar.PingRateLimit)), "the first calls of a burst are the ones accepted");
            Assert.That(_hostRejections.All(r => r.Verdict == RpcVerdict.RateLimited && r.SenderClientId == _a.LocalClientId), Is.True,
                "only Ann was throttled; Bob's budget is his own");
        }

        [UnityTest]
        public IEnumerator ToServer_ASendersBudgetIsForgottenWhenItLeaves()
        {
            yield return StartWithAvatar();
            for (int i = 0; i < 20; i++)
            {
                _onB.Ping(i);
            }

            yield return WaitUntil(() => _hostRejections.Count > 0, "Bob was never throttled");
            Assert.That(_host.Gate.Limiter.RejectedCount(_b.LocalClientId), Is.GreaterThan(0));
            ulong bobId = _b.LocalClientId;

            _b.Leave();
            yield return WaitUntil(() => _host.ConnectedClientIds.Count == 2, "the host never saw Bob leave");
            Assert.That(_host.Gate.Limiter.RejectedCount(bobId), Is.EqualTo(0), "a departed sender's buckets are dropped");
        }

        // ---------------------------------------------------------------------------------------------------------
        // [ToClients] / [ToOwner] / [ToClient]
        // ---------------------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator ToClients_RunsOnEveryClientAndTheHost()
        {
            yield return StartWithAvatar();
            var red = new Color32(255, 0, 0, 255);

            _onHost.Whistle(red);

            yield return WaitUntil(() => _onHost.Whistles.Count == 1 && _onA.Whistles.Count == 1 && _onB.Whistles.Count == 1, () => "Whistle did not reach every peer (host " + _onHost.Whistles.Count + ", Ann " + _onA.Whistles.Count + ", Bob " + _onB.Whistles.Count + ")");
            Assert.That(_onB.Whistles[0], Is.EqualTo(red));
            yield return Settle();
            Assert.That(_onA.Whistles.Count, Is.EqualTo(1), "exactly once each");
        }

        [UnityTest]
        public IEnumerator ServerToClientCalls_FromAClient_AreNotSent()
        {
            yield return StartWithAvatar();

            _onA.Whistle(new Color32(1, 2, 3, 255));
            _onA.Hint(5);
            _onA.Tell(_b.LocalClientId, 6);

            Assert.That(_aRejections.Select(r => r.Verdict), Is.EqualTo(new[] { RpcVerdict.SentFromClient, RpcVerdict.SentFromClient, RpcVerdict.SentFromClient }),
                "the client's own session reports each refused send");
            yield return Settle();
            Assert.That(_onHost.Whistles.Count + _onA.Whistles.Count + _onB.Whistles.Count, Is.EqualTo(0));
            Assert.That(_onA.Hints.Count + _onHost.Hints.Count, Is.EqualTo(0));
            Assert.That(_onB.Tells, Is.Empty);
        }

        [UnityTest]
        public IEnumerator ToOwner_RunsOnlyOnTheOwner()
        {
            yield return StartWithAvatar();

            _onHost.Hint(42);

            yield return WaitUntil(() => _onA.Hints.Count == 1, "Hint never reached the owner");
            Assert.That(_onA.Hints[0], Is.EqualTo(42));
            yield return Settle();
            Assert.That(_onB.Hints, Is.Empty, "Bob does not own it");
            Assert.That(_onHost.Hints, Is.Empty, "neither does the host");
        }

        [UnityTest]
        public IEnumerator ToClient_RunsOnlyOnTheTargetClient()
        {
            yield return StartWithAvatar();

            _onHost.Tell(_b.LocalClientId, 7);
            _onHost.Tell(NetSender.ServerClientId, 8);

            yield return WaitUntil(() => _onB.Tells.Count == 1 && _onHost.Tells.Count == 1, () => "Tell did not reach its targets (Bob " + _onB.Tells.Count + ", host " + _onHost.Tells.Count + ")");
            Assert.That(_onB.Tells[0], Is.EqualTo(7));
            Assert.That(_onHost.Tells[0], Is.EqualTo(8), "the host's own player can be a target");
            yield return Settle();
            Assert.That(_onA.Tells, Is.Empty, "Ann was not a target");
        }

        [UnityTest]
        public IEnumerator AServerHandler_CanAnswerItsCaller()
        {
            yield return StartWithAvatar();

            _onB.Vote(3); // OnVote answers with Tell(sender, 103)

            yield return WaitUntil(() => _onB.Tells.Count == 1, "the server's answer never reached Bob");
            Assert.That(_onB.Tells[0], Is.EqualTo(103));
            yield return Settle();
            Assert.That(_onA.Tells, Is.Empty);
        }

        // ---------------------------------------------------------------------------------------------------------
        // [Synced]
        // ---------------------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator Synced_ServerWrites_EveryoneReads_AndOnChangedRunsEverywhere()
        {
            yield return StartWithAvatar();

            _onHost.Mass = 2.5f;

            yield return WaitUntil(() => Mathf.Approximately(_onA.Mass, 2.5f) && Mathf.Approximately(_onB.Mass, 2.5f), () => "Mass never replicated (Ann " + _onA.Mass + ", Bob " + _onB.Mass + ")");
            yield return WaitUntil(() => _onA.MassChanges.Count == 1 && _onB.MassChanges.Count == 1, "OnChanged did not run on the clients");
            Assert.That(_onHost.MassChanges, Is.EqualTo(new[] { new Vector2(0, 2.5f) }), "OnChanged runs on the server too");
            Assert.That(_onA.MassChanges[0], Is.EqualTo(new Vector2(0, 2.5f)), "previous and current value");
        }

        [UnityTest]
        public IEnumerator Synced_AClientWrite_Throws_AndChangesNothing()
        {
            yield return StartWithAvatar();
            _onHost.Score = 3;
            yield return WaitUntil(() => _onA.Score == 3, "Score never replicated");

            Assert.Throws<System.InvalidOperationException>(() => _onA.Score = 99);

            yield return Settle();
            Assert.That(_onA.Score, Is.EqualTo(3));
            Assert.That(_onHost.Score, Is.EqualTo(3));
        }

        [UnityTest]
        public IEnumerator Synced_ALateJoinerGetsTheCurrentValue()
        {
            yield return StartWithAvatar(joinBob: false);
            _onHost.Score = 42;
            _onHost.Mass = 1.25f;

            yield return Join(_b);
            ulong id = _onHost.NetworkObjectId;
            yield return WaitUntil(() => SeenBy(m_ClientNetworkManagers[1], id), "Bob never saw the avatar");
            TestAvatar onB = Copy(m_ClientNetworkManagers[1], id);

            Assert.That(onB.Score, Is.EqualTo(42));
            Assert.That(onB.Mass, Is.EqualTo(1.25f));
            Assert.That(onB.Spawns, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Despawn_RunsOnDespawnedEverywhere()
        {
            yield return StartWithAvatar();
            _onHost.NetworkObject.Despawn(false);
            yield return WaitUntil(() => _onA.Despawns == 1 && _onB.Despawns == 1, "OnDespawned did not run on the clients");
            Assert.That(_onHost.Despawns, Is.EqualTo(1));
            Object.Destroy(_onHost.gameObject);
        }

        // ---------------------------------------------------------------------------------------------------------
        // Prefab registry
        // ---------------------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator PrefabRegistry_RefusesPrefabsWithoutNetworkObject_AndLateRegistrations()
        {
            var plain = new GameObject("NoNetworkObject");
            Assert.Throws<System.ArgumentException>(() => _host.Prefabs.Register(plain));
            Assert.Throws<System.ArgumentNullException>(() => _host.Prefabs.Register((GameObject)null));

            var list = ScriptableObject.CreateInstance<NetPrefabList>();
            var late = new GameObject("Late");
            NetcodeIntegrationTestHelpers.MakeNetworkObjectTestPrefab(late.AddComponent<NetworkObject>());
            list.Add(late);
            list.Add(_prefab);

            yield return StartWithAvatar(joinBob: false);

            Assert.Throws<System.InvalidOperationException>(() => _host.Prefabs.Register(list), "every peer registers before it starts");
            Assert.That(_host.Prefabs.Contains(late), Is.False);
            Assert.That(_host.Prefabs.Prefabs, Does.Contain(_prefab));
            Assert.That(_b.Prefabs.Register(list), Is.EqualTo(1), "a peer that has not started can still register; the known prefab is skipped");

            Object.Destroy(plain);
            Object.Destroy(late);
            Object.Destroy(list);
        }
    }
}
