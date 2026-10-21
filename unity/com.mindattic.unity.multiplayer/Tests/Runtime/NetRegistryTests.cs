using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Multiplayer.Core;
using Unity.Multiplayer.Testing;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.Multiplayer.Tests
{
    /// <summary>
    /// UM-T60: networked objects found by a game-given key on every peer, over a real Unity Transport on loopback. A Host
    /// and two clients (the second joins late in some tests); keyed objects are a <see cref="KeyedProbe"/> plus a
    /// <see cref="NetKey"/>, each placed at its own x so a peer can tell which object it really got for a key.
    /// </summary>
    [TestFixture(Topology.Host)]
    [TestFixture(Topology.Server)]
    public class NetRegistryTests : NetSessionTest
    {
        private GameObject _prefab;
        private GameObject _fixedKeyPrefab;
        private readonly Dictionary<NetSession, List<string>> _spawned = new Dictionary<NetSession, List<string>>();
        private readonly Dictionary<NetSession, List<string>> _despawned = new Dictionary<NetSession, List<string>>();

        public NetRegistryTests(Topology topology) : base(topology, clients: 2)
        {
        }

        protected override string GameVersion => "um-t60";

        protected override void OnSessionsCreated()
        {
            // The probe comes first, so its OnSpawned runs before the NetKey's: the key must already be there.
            _prefab = CreatePrefab<KeyedProbe>("Keyed");
            _prefab.AddComponent<NetKey>();

            _fixedKeyPrefab = CreatePrefab<NetKey>("FixedKey");
            _fixedKeyPrefab.GetComponent<NetKey>().Key = "scoreboard";

            foreach (NetSession session in Sessions)
            {
                NetSession peer = session;
                var spawned = new List<string>();
                var despawned = new List<string>();
                _spawned[peer] = spawned;
                _despawned[peer] = despawned;
                peer.Objects.Spawned += key => spawned.Add(key.Key);
                peer.Objects.Despawned += key => despawned.Add(key.Key);
            }
        }

        protected override IEnumerator OnSessionsTearDown()
        {
            _spawned.Clear();
            _despawned.Clear();
            yield return null;
        }

        private NetSession Ann => Clients[0];

        private NetSession Bob => Clients[1];

        private GameObject SpawnKeyed(string key, float x)
        {
            return Server.Objects.Spawn(_prefab, key, new Vector3(x, 0f, 0f));
        }

        private IEnumerator WaitForKeys(NetSession peer, string who, params string[] keys)
        {
            yield return WaitUntil(
                () => keys.All(peer.Objects.Contains),
                () => who + " never had keys " + string.Join(", ", keys) + " (has " + string.Join(", ", peer.Objects.Keys) + ")");
        }

        private static ulong IdOf(NetSession peer, string key)
        {
            return peer.Objects.Get<NetKey>(key).NetworkObjectId;
        }

        [UnityTest]
        public IEnumerator EveryPeer_FindsEachObjectByItsKey()
        {
            yield return StartAndJoinAll();

            GameObject b = SpawnKeyed("block:b", 2f);
            GameObject a = SpawnKeyed("block:a", 1f);
            GameObject shape = SpawnKeyed("shape 1", 3f);

            Assert.That(Server.Objects.Count, Is.EqualTo(3));
            Assert.That(Server.Objects.TryGet("block:a", out GameObject onServer), Is.True);
            Assert.That(onServer, Is.SameAs(a));
            Assert.That(Server.Objects.Get<KeyedProbe>("block:b").gameObject, Is.SameAs(b));
            Assert.That(Server.Objects.Get<NetKey>("shape 1").gameObject, Is.SameAs(shape));

            foreach (NetSession client in Clients)
            {
                string who = client == Ann ? "Ann" : "Bob";
                yield return WaitForKeys(client, who, "block:a", "block:b", "shape 1");

                var xs = new Dictionary<string, float> { { "block:a", 1f }, { "block:b", 2f }, { "shape 1", 3f } };
                foreach (KeyValuePair<string, float> pair in xs)
                {
                    string key = pair.Key;
                    Assert.That(client.Objects.TryGet(key, out KeyedProbe probe), Is.True, who + " finds " + key);
                    Assert.That(probe.NetworkObjectId, Is.EqualTo(IdOf(Server, key)), who + "'s " + key + " is the server's " + key);
                    Assert.That(probe.KeyAtSpawn, Is.EqualTo(key), who + "'s components see the key in their own OnSpawned");
                    Assert.That(probe.transform.position.x, Is.EqualTo(pair.Value).Within(0.01f), who + "'s " + key + " is where the server put it");
                }

                Assert.That(client.Objects.Count, Is.EqualTo(3));
                Assert.That(client.Objects.TryGet("block:c", out KeyedProbe missing), Is.False);
                Assert.That(missing, Is.Null);
                var notFound = Assert.Throws<KeyNotFoundException>(() => client.Objects.Get<NetKey>("block:c"));
                Assert.That(notFound.Message, Does.Contain("\"block:c\""));
                Assert.That(_spawned[client], Is.EquivalentTo(new[] { "block:a", "block:b", "shape 1" }));

                NetKey clientKey = client.Objects.Get<NetKey>("block:a");
                Assert.Throws<InvalidOperationException>(() => clientKey.Key = "other", "a spawned key never changes");
                Assert.Throws<InvalidOperationException>(() => client.Objects.Spawn(_prefab, "from-client"), "only the server spawns");
            }

            Assert.Throws<InvalidOperationException>(() => a.GetComponent<NetKey>().Key = "block:z", "not even on the server");
        }

        [UnityTest]
        public IEnumerator ALateJoiner_FindsObjectsSpawnedBeforeItCame()
        {
            yield return StartServer();
            yield return Join(Ann);

            SpawnKeyed("b1", 1f);
            SpawnKeyed("b2", 2f);
            yield return WaitForKeys(Ann, "Ann", "b1", "b2");
            Assert.That(Bob.Objects.Count, Is.EqualTo(0));

            yield return Join(Bob);
            yield return WaitForKeys(Bob, "Bob (late)", "b1", "b2");

            Assert.That(_spawned[Bob], Is.EquivalentTo(new[] { "b1", "b2" }), "the late joiner hears of each object already there");
            Assert.That(Bob.Objects.Get<KeyedProbe>("b1").KeyAtSpawn, Is.EqualTo("b1"));
            Assert.That(IdOf(Bob, "b1"), Is.EqualTo(IdOf(Server, "b1")));
            Assert.That(IdOf(Bob, "b2"), Is.EqualTo(IdOf(Server, "b2")));
            Assert.That(Bob.Objects.Get<KeyedProbe>("b2").transform.position.x, Is.EqualTo(2f).Within(0.01f));

            // Objects spawned after the late join reach it too.
            SpawnKeyed("b3", 3f);
            yield return WaitForKeys(Bob, "Bob", "b3");
            yield return WaitForKeys(Ann, "Ann", "b3");
        }

        [UnityTest]
        public IEnumerator SpawnOrder_DoesNotDecideWhichObjectIsWhich()
        {
            yield return StartAndJoinAll();

            // A level's order is a, b, c, but the server spawns them out of that order, then replaces b, so neither
            // network object ids nor spawn order line up with the level any more.
            SpawnKeyed("c", 3f);
            SpawnKeyed("a", 1f);
            SpawnKeyed("b", 2f);
            yield return WaitForKeys(Ann, "Ann", "a", "b", "c");

            Assert.That(Server.Objects.Despawn("b"), Is.True);
            SpawnKeyed("b", 2.5f);

            var expected = new Dictionary<string, float> { { "a", 1f }, { "b", 2.5f }, { "c", 3f } };
            foreach (NetSession client in Clients)
            {
                yield return WaitUntil(
                    () => client.Objects.Contains("b") && IdOf(client, "b") == IdOf(Server, "b"),
                    "a client never got the replacement b");

                foreach (KeyValuePair<string, float> pair in expected)
                {
                    KeyedProbe probe = client.Objects.Get<KeyedProbe>(pair.Key);
                    Assert.That(probe.transform.position.x, Is.EqualTo(pair.Value).Within(0.01f), pair.Key + " is the object spawned as " + pair.Key);
                    Assert.That(probe.NetworkObjectId, Is.EqualTo(IdOf(Server, pair.Key)));
                }
            }

            ulong[] ids = expected.Keys.Select(key => IdOf(Server, key)).ToArray();
            Assert.That(ids, Is.Not.Ordered, "the test really spawned out of order");
        }

        [UnityTest]
        public IEnumerator Despawn_IsHeardEverywhere_AndFreesTheKey()
        {
            yield return StartAndJoinAll();

            GameObject first = SpawnKeyed("door", 1f);
            yield return WaitForKeys(Ann, "Ann", "door");
            yield return WaitForKeys(Bob, "Bob", "door");
            KeyedProbe annFirst = Ann.Objects.Get<KeyedProbe>("door");

            Assert.That(Server.Objects.Despawn("door"), Is.True);
            Assert.That(Server.Objects.Contains("door"), Is.False, "the server frees the key at once");
            Assert.That(Server.Objects.Despawn("door"), Is.False, "nothing left to despawn");
            Assert.That(_despawned[Server], Is.EqualTo(new[] { "door" }));

            foreach (NetSession client in Clients)
            {
                yield return WaitUntil(() => !client.Objects.Contains("door"), "a client still has the door");
                Assert.That(_despawned[client], Is.EqualTo(new[] { "door" }));
            }

            Assert.That(annFirst == null || annFirst.Despawns == 1, Is.True, "Ann's copy despawned once");

            // Respawned under the same key in the same frame as another despawn: the key now names the new object.
            GameObject second = SpawnKeyed("door", 2f);
            GameObject window = SpawnKeyed("window", 3f);
            Assert.That(Server.Objects.Despawn("window"), Is.True);
            GameObject window2 = SpawnKeyed("window", 4f);
            Assert.That(first == null || !first.GetComponent<NetworkObject>().IsSpawned, Is.True);

            foreach (NetSession client in Clients)
            {
                yield return WaitUntil(
                    () => client.Objects.Contains("door") && client.Objects.Contains("window") && IdOf(client, "window") == window2.GetComponent<NetworkObject>().NetworkObjectId,
                    "a client never settled on the new door and window");
                Assert.That(IdOf(client, "door"), Is.EqualTo(second.GetComponent<NetworkObject>().NetworkObjectId));
                Assert.That(client.Objects.Get<KeyedProbe>("window").transform.position.x, Is.EqualTo(4f).Within(0.01f));
            }

            Assert.That(window == null || !window.GetComponent<NetworkObject>().IsSpawned, Is.True);
        }

        [UnityTest]
        public IEnumerator ADuplicateKey_IsRefusedWithAClearError()
        {
            yield return StartAndJoinAll();

            GameObject first = SpawnKeyed("b1", 1f);
            yield return WaitForKeys(Ann, "Ann", "b1");

            var refused = Assert.Throws<ArgumentException>(() => SpawnKeyed("b1", 9f));
            StringAssert.Contains("\"b1\"", refused.Message);
            StringAssert.Contains("already used by Keyed", refused.Message);
            Assert.That(Server.Objects.Get<NetKey>("b1").gameObject, Is.SameAs(first));
            Assert.That(Server.Objects.Count, Is.EqualTo(1));

            Assert.Throws<ArgumentException>(() => Server.Objects.Spawn(_prefab, " "), "an unusable key is refused too");
            var unkeyed = new GameObject("Unkeyed");
            unkeyed.AddComponent<NetworkObject>();
            var noKey = Assert.Throws<ArgumentException>(() => Server.Objects.Spawn(unkeyed, "x"));
            StringAssert.Contains("needs a NetworkObject and a NetKey", noKey.Message);
            UnityEngine.Object.Destroy(unkeyed);

            // Spawned some other way (the kit's Spawn, like NetworkObject.Spawn) with a key already in use: the first object
            // keeps the key and every peer reports the second as an error.
            Server.Objects.Spawn(_fixedKeyPrefab, "scoreboard");
            yield return WaitForKeys(Ann, "Ann", "scoreboard");
            yield return WaitForKeys(Bob, "Bob", "scoreboard");
            ulong holder = IdOf(Server, "scoreboard");

            var duplicate = new Regex("Network key \"scoreboard\" is already used by FixedKey.*was not given it");
            LogAssert.Expect(LogType.Error, duplicate); // the host
            LogAssert.Expect(LogType.Error, duplicate); // Ann
            LogAssert.Expect(LogType.Error, duplicate); // Bob
            NetKey second = Spawn<NetKey>(_fixedKeyPrefab);

            Assert.That(second.IsRegistered, Is.False);
            Assert.That(IdOf(Server, "scoreboard"), Is.EqualTo(holder));
            foreach (NetSession client in Clients)
            {
                NetSession peer = client;
                yield return WaitUntil(
                    () => peer.Objects.Count == 2 && CopyOf(peer, second.NetworkObjectId) != null,
                    "a client never got the second scoreboard");
                Assert.That(IdOf(peer, "scoreboard"), Is.EqualTo(holder), "the first object keeps the key");
                Assert.That(CopyOf(peer, second.NetworkObjectId).IsRegistered, Is.False);
            }

            // Once the holder goes, the key is free again.
            Assert.That(Server.Objects.Despawn("scoreboard"), Is.True);
            Assert.That(Server.Objects.Contains("scoreboard"), Is.False);
        }

        /// <summary>A peer's copy of the object with <paramref name="networkObjectId"/>, keyed or not; null before it arrives.</summary>
        private static NetKey CopyOf(NetSession peer, ulong networkObjectId)
        {
            NetworkObject found;
            NetworkManager manager = TestSessions.ManagerOf(peer);
            return manager.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out found) ? found.GetComponent<NetKey>() : null;
        }
    }
}
