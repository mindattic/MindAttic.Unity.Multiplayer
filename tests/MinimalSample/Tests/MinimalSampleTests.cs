using System.Collections;
using System.Reflection;
using MindAttic.Unity.Multiplayer.Core;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.TestHelpers.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using SessionState = MindAttic.Unity.Multiplayer.Core.SessionState;

namespace MindAttic.Unity.Multiplayer.Samples.Minimal.Tests
{
    /// <summary>
    /// UM-T16: the Minimal sample as Package Manager imports it (run-sample.ps1 imports it into tests/SampleHost and
    /// copies these tests beside it). Finds the imported scene and prefab under Assets/Samples.
    /// </summary>
    internal static class SampleAssets
    {
        public const string Root = "Assets/Samples";

        public static string Path(string filter, string fileName)
        {
            foreach (string guid in AssetDatabase.FindAssets(filter, new[] { Root }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith("/Minimal/" + fileName))
                {
                    return path;
                }
            }

            Assert.Fail("The imported Minimal sample has no " + fileName + " under " + Root + ".");
            return null;
        }

        /// <summary>When the last bump's flash ends on this copy (set by OnBumped, i.e. the [ToClients] call arrived).</summary>
        public static float FlashUntil(MinimalPlayer player)
        {
            return (float)typeof(MinimalPlayer).GetField("_flashUntil", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(player);
        }

        /// <summary>The direction the server accepted from the owner (OnSetDirection, i.e. the [ToServer] call arrived).</summary>
        public static Vector2 AcceptedDirection(MinimalPlayer player)
        {
            return (Vector2)typeof(MinimalPlayer).GetField("_direction", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(player);
        }
    }

    /// <summary>The sample scene itself: wired as the README says, hosts from its NetBootstrap, and a player moves.</summary>
    public class MinimalSceneTests
    {
        private NetBootstrap _bootstrap;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_bootstrap != null && _bootstrap.Session != null)
            {
                _bootstrap.Leave();
                float until = Time.realtimeSinceStartup + 5f;
                while (_bootstrap.Session.State != SessionState.Idle && Time.realtimeSinceStartup < until)
                {
                    yield return null;
                }
            }

            Scene scratch = SceneManager.CreateScene("Scratch" + Time.frameCount);
            SceneManager.SetActiveScene(scratch);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene != scratch)
                {
                    yield return SceneManager.UnloadSceneAsync(scene);
                }
            }
        }

        [UnityTest]
        public IEnumerator MinimalScene_HostsFromTheMenuItsPlayerSpawnsMovesAndBumpsTheWall()
        {
            string scenePath = SampleAssets.Path("Minimal t:Scene", "Minimal.unity");
            AsyncOperation load = EditorSceneManager.LoadSceneAsyncInPlayMode(scenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return load;
            yield return null;

            _bootstrap = NetBootstrap.Instance;
            Assert.That(_bootstrap, Is.Not.Null, "the scene has a NetBootstrap");
            NetworkManager manager = _bootstrap.GetComponent<NetworkManager>();
            Assert.That(manager.NetworkConfig.PlayerPrefab, Is.Not.Null, "the NetworkManager has a player prefab");
            Assert.That(manager.NetworkConfig.PlayerPrefab.GetComponent<MinimalPlayer>(), Is.Not.Null, "the player prefab is the MinimalPlayer");
            Assert.That(manager.NetworkConfig.EnableSceneManagement, Is.False, "one scene: scene management is off");
            Assert.That(Object.FindAnyObjectByType<MinimalMenu>(), Is.Not.Null, "the scene has the menu");
            if (HeadlessBootstrap.IsHeadless)
            {
                // A batch-mode run (run-sample.ps1) is headless, so the scene serves by itself as a dedicated server,
                // as the README says. Leave it and host from the menu's call instead.
                Assert.That(_bootstrap.Session.IsServer && !_bootstrap.Session.IsHost, Is.True, "a headless launch serves as a dedicated server");
                _bootstrap.Leave();
                yield return Until(() => _bootstrap.Session.State == SessionState.Idle, "the dedicated server never stopped");
            }
            else
            {
                Assert.That(_bootstrap.Session.IsActive, Is.False, "nothing starts until a button is pressed");
            }

            Assert.That(_bootstrap.StartHost(), Is.True, "Host");
            yield return Until(() => manager.LocalClient != null && manager.LocalClient.PlayerObject != null, "the host's player never spawned");
            Assert.That(_bootstrap.Session.State, Is.EqualTo(SessionState.Running));

            MinimalPlayer player = manager.LocalClient.PlayerObject.GetComponent<MinimalPlayer>();
            Assert.That(player.IsOwner, Is.True);
            Assert.That(player.Position, Is.EqualTo(new Vector2(-3f, 1.5f)), "the first seat starts at (-3, 1.5)");

            player.SetDirection(Vector2.right);
            yield return Until(() => Mathf.Approximately(player.Position.x, MinimalPlayer.ArenaHalfSize), "the player never reached the right wall");
            Assert.That(player.Position.y, Is.EqualTo(1.5f).Within(0.001f), "it moved straight right");
            Assert.That(SampleAssets.FlashUntil(player), Is.GreaterThan(0f), "hitting the wall sent Bumped");
            yield return null;
            Assert.That((Vector2)player.transform.position, Is.EqualTo(player.Position), "the square shows the synced position");
        }

        private static IEnumerator Until(System.Func<bool> condition, string failure)
        {
            float until = Time.realtimeSinceStartup + 10f;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > until)
                {
                    Assert.Fail(failure);
                }

                yield return null;
            }
        }
    }

    /// <summary>
    /// The sample's player prefab between a host and a client over Unity Transport on loopback: the [ToServer] call from
    /// the owner, the [Synced] position on both peers, the [ToClients] bump on both, and a non-owner refused.
    /// </summary>
    public class MinimalPlayerNetworkTests : NetcodeIntegrationTest
    {
        private const ushort Port = 47911;

        private NetSession _host;
        private NetSession _client;
        private int _hostRejections;

        public MinimalPlayerNetworkTests() : base(HostOrServer.Host)
        {
        }

        protected override int NumberOfClients => 1;

        protected override bool CanStartServerAndClients() => false;

        protected override void OnServerAndClientsCreated()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SampleAssets.Path("MinimalPlayer t:Prefab", "MinimalPlayer.prefab"));
            Assert.That(prefab, Is.Not.Null);
            foreach (NetworkManager manager in m_NetworkManagers)
            {
                manager.NetworkConfig.EnableSceneManagement = false; // as in the sample scene
                manager.NetworkConfig.PlayerPrefab = prefab;
            }

            _host = new NetSession(m_ServerNetworkManager) { Identity = new PlayerIdentity("host-token", "Host", PlayerColor.White) };
            _client = new NetSession(m_ClientNetworkManagers[0]) { Identity = new PlayerIdentity("ann-token", "Ann", PlayerColor.White) };
            _host.RpcRejected += r => _hostRejections++;
            base.OnServerAndClientsCreated();
        }

        protected override IEnumerator OnTearDown()
        {
            _client?.Dispose();
            _host?.Dispose();
            _client = _host = null;
            _hostRejections = 0;
            yield return null;
        }

        [UnityTest]
        public IEnumerator OwnerSteers_ServerMoves_EveryPeerSeesThePositionAndTheBump()
        {
            var config = new NetConfig { Port = Port, GameVersion = "1" };
            Assert.That(_host.StartHost(config), Is.True, "StartHost");
            yield return WaitForConditionOrTimeOut(() => _host.State == SessionState.Running);
            AssertOnTimeout("the host never reached Running");
            Assert.That(_client.JoinByAddress(NetConfig.LoopbackAddress, Port, config), Is.True, "JoinByAddress");

            NetworkManager clientManager = m_ClientNetworkManagers[0];
            yield return WaitForConditionOrTimeOut(() => _client.State == SessionState.Connected
                && clientManager.LocalClient != null && clientManager.LocalClient.PlayerObject != null);
            AssertOnTimeout("the client's player never spawned on the client (" + _client.State + ", " + _client.LastDisconnect + ")");

            MinimalPlayer mine = clientManager.LocalClient.PlayerObject.GetComponent<MinimalPlayer>();
            ulong id = mine.NetworkObjectId;
            yield return WaitForConditionOrTimeOut(() => m_ServerNetworkManager.SpawnManager.SpawnedObjects.ContainsKey(id));
            AssertOnTimeout("the host never had the client's player");
            MinimalPlayer onHost = m_ServerNetworkManager.SpawnManager.SpawnedObjects[id].GetComponent<MinimalPlayer>();
            Assert.That(mine.IsOwner, Is.True, "the client owns its player");
            Assert.That(onHost.IsOwner, Is.False);

            // The client's own copy shows the spot the server picked for its seat.
            yield return WaitForConditionOrTimeOut(() => mine.Position == onHost.Position && mine.Position != Vector2.zero);
            AssertOnTimeout("the starting position never reached the client");

            // [ToServer]: the owner asks for a too-long vector; the server accepts it clamped to length 1.
            mine.SetDirection(new Vector2(-5f, 0f));
            yield return WaitForConditionOrTimeOut(() => SampleAssets.AcceptedDirection(onHost) != Vector2.zero);
            AssertOnTimeout("SetDirection never reached the server");
            Assert.That(SampleAssets.AcceptedDirection(onHost), Is.EqualTo(Vector2.left), "the server clamps the direction");

            // [Synced]: the server moves it to the left wall and the client sees the same position.
            yield return WaitForConditionOrTimeOut(() => Mathf.Approximately(onHost.Position.x, -MinimalPlayer.ArenaHalfSize)
                && mine.Position == onHost.Position);
            AssertOnTimeout("the client never saw its player at the left wall (" + mine.Position + " / " + onHost.Position + ")");

            // [ToClients]: hitting the wall flashed the square on the host and on the client.
            yield return WaitForConditionOrTimeOut(() => SampleAssets.FlashUntil(mine) > 0f && SampleAssets.FlashUntil(onHost) > 0f);
            AssertOnTimeout("Bumped never arrived on both peers");

            // Only the owner steers: the client steering the host's player is refused by the server.
            GameObject hostPlayer = m_ServerNetworkManager.LocalClient.PlayerObject.gameObject;
            ulong hostPlayerId = hostPlayer.GetComponent<NetworkObject>().NetworkObjectId;
            yield return WaitForConditionOrTimeOut(() => clientManager.SpawnManager.SpawnedObjects.ContainsKey(hostPlayerId));
            AssertOnTimeout("the client never saw the host's player");
            clientManager.SpawnManager.SpawnedObjects[hostPlayerId].GetComponent<MinimalPlayer>().SetDirection(Vector2.up);
            yield return WaitForConditionOrTimeOut(() => _hostRejections > 0);
            AssertOnTimeout("the server never refused a non-owner");
            Assert.That(SampleAssets.AcceptedDirection(hostPlayer.GetComponent<MinimalPlayer>()), Is.EqualTo(Vector2.zero), "the host's player did not move");
        }
    }
}
