using System.Collections;
using System.Net;
using System.Net.Sockets;
using NUnit.Framework;
using Unity.Multiplayer.Core;
using Unity.Multiplayer.UI;
using Unity.Netcode;
using Unity.Netcode.TestHelpers.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using SessionState = Unity.Multiplayer.Core.SessionState;

namespace Unity.Multiplayer.Samples.FrontEnd.Tests
{
    /// <summary>
    /// UM-T48: the Front End sample as Package Manager imports it (run-sample.ps1 -Sample FrontEnd imports it into
    /// tests/SampleHost, with MindAttic Components and Unity.Shared, and copies these tests beside it).
    /// </summary>
    internal static class SampleAssets
    {
        public const string Root = "Assets/Samples";

        public static string Path(string filter, string fileName)
        {
            foreach (string guid in AssetDatabase.FindAssets(filter, new[] { Root }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith("/Front End/" + fileName))
                {
                    return path;
                }
            }

            Assert.Fail("The imported Front End sample has no " + fileName + " under " + Root + ".");
            return null;
        }

        public static ushort FreeUdpPort()
        {
            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                socket.Bind(new IPEndPoint(IPAddress.Any, 0));
                return (ushort)((IPEndPoint)socket.LocalEndPoint).Port;
            }
        }

        public static IEnumerator Until(System.Func<bool> condition, string failure)
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

    /// <summary>The sample scene: wired by the Add Front End step, and a headless launch serves with no UI at all.</summary>
    public class FrontEndSceneTests
    {
        private NetBootstrap _bootstrap;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_bootstrap != null && _bootstrap.Session != null)
            {
                _bootstrap.Leave();
                yield return SampleAssets.Until(() => _bootstrap.Session.State == SessionState.Idle, "the session never stopped");
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
        public IEnumerator FrontEndScene_IsWiredByTheWizard_AndAHeadlessLaunchServesWithNoMenus()
        {
            string scenePath = SampleAssets.Path("FrontEnd t:Scene", "FrontEnd.unity");
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(scenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;

            _bootstrap = NetBootstrap.Instance;
            Assert.That(_bootstrap, Is.Not.Null, "the scene has a NetBootstrap");
            NetFrontEnd frontEnd = _bootstrap.GetComponent<NetFrontEnd>();
            Assert.That(frontEnd, Is.Not.Null, "Add Front End put the NetFrontEnd beside the NetBootstrap");
            Assert.That(frontEnd.Session, Is.SameAs(_bootstrap.Session));
            NetworkManager manager = _bootstrap.GetComponent<NetworkManager>();
            Assert.That(manager.NetworkConfig.PlayerPrefab.GetComponent<FrontEndPlayer>(), Is.Not.Null, "the player prefab is the FrontEndPlayer");
            Assert.That(Object.FindAnyObjectByType<FrontEndGame>(), Is.Not.Null, "the scene has the game's hooks");

            if (HeadlessBootstrap.IsHeadless)
            {
                yield return SampleAssets.Until(() => _bootstrap.Session.State == SessionState.Running, "the headless scene never served");
                Assert.That(_bootstrap.Session.IsServer && !_bootstrap.Session.IsHost, Is.True, "a headless launch is a dedicated server");
                Assert.That(frontEnd.HasMenus, Is.False, "a headless launch shows no menus");
                Assert.That(Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include), Is.Empty, "no UI at all");
                Assert.That(frontEnd.State, Is.EqualTo(FrontEndState.InGame), "the front end still follows the session");
            }
            else
            {
                Assert.That(frontEnd.HasMenus, Is.True);
                Assert.That(frontEnd.MainMenu.IsOpen, Is.True, "the main menu is up");
            }
        }
    }

    /// <summary>
    /// The sample's player between a host and a client over loopback: each player's square takes the name and colour
    /// that player sent when joining (the front end's identity), and every peer sees them.
    /// </summary>
    public class FrontEndPlayerNetworkTests : NetcodeIntegrationTest
    {
        private NetSession _host;
        private NetSession _client;
        private ushort _port;

        public FrontEndPlayerNetworkTests() : base(HostOrServer.Host)
        {
        }

        protected override int NumberOfClients => 1;

        protected override bool CanStartServerAndClients() => false;

        protected override void OnServerAndClientsCreated()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SampleAssets.Path("FrontEndPlayer t:Prefab", "FrontEndPlayer.prefab"));
            Assert.That(prefab, Is.Not.Null);
            foreach (NetworkManager manager in m_NetworkManagers)
            {
                manager.NetworkConfig.EnableSceneManagement = false;
                manager.NetworkConfig.PlayerPrefab = prefab;
            }

            _port = SampleAssets.FreeUdpPort();
            _host = new NetSession(m_ServerNetworkManager) { Identity = new PlayerIdentity("ann-token", "Ann", new PlayerColor(230, 75, 60)) };
            _client = new NetSession(m_ClientNetworkManagers[0]) { Identity = new PlayerIdentity("bob-token", "Bob", new PlayerColor(52, 152, 219)) };
            base.OnServerAndClientsCreated();
        }

        protected override IEnumerator OnTearDown()
        {
            _client?.Dispose();
            _host?.Dispose();
            _client = _host = null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator EachSquare_CarriesItsPlayersNameAndColour_OnEveryPeer()
        {
            var config = new NetConfig { Port = _port, GameVersion = "1", JoinCodes = JoinCodeProvider.None };
            Assert.That(_host.StartHost(config), Is.True);
            yield return SampleAssets.Until(() => _host.State == SessionState.Running, "the host never ran");
            Assert.That(_client.JoinByAddress(NetConfig.LoopbackAddress, _port, config), Is.True);

            NetworkManager clientManager = m_ClientNetworkManagers[0];
            yield return SampleAssets.Until(() => clientManager.LocalClient != null && clientManager.LocalClient.PlayerObject != null
                && clientManager.SpawnManager.SpawnedObjectsList.Count == 2, "the client never saw both squares");

            FrontEndPlayer bobOnClient = clientManager.LocalClient.PlayerObject.GetComponent<FrontEndPlayer>();
            yield return SampleAssets.Until(() => bobOnClient.Name.ToString() == "Bob", "Bob's name never reached his own client");
            Assert.That(bobOnClient.Color, Is.EqualTo(new Color32(52, 152, 219, 255)), "Bob's colour");

            FrontEndPlayer annOnClient = null;
            foreach (NetworkObject spawned in clientManager.SpawnManager.SpawnedObjectsList)
            {
                if (spawned != clientManager.LocalClient.PlayerObject)
                {
                    annOnClient = spawned.GetComponent<FrontEndPlayer>();
                }
            }

            Assert.That(annOnClient, Is.Not.Null);
            Assert.That(annOnClient.Name.ToString(), Is.EqualTo("Ann"), "the host's square, seen by the client");
            Assert.That(annOnClient.Color, Is.EqualTo(new Color32(230, 75, 60, 255)));
        }
    }
}
