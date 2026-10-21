using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Multiplayer.Core;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Multiplayer.Testing;
using Unity.Netcode.TestHelpers.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Unity.Multiplayer.Tests
{
    /// <summary>
    /// UM-T10: server-driven scene flow through <see cref="NetSceneFlow"/>, over a real Unity Transport on loopback. A
    /// Host and two Clients with NGO scene management on. In one process the clients' loads are simulated by NGO's
    /// integration-test scene handler, registered by hand because NetSession, not the harness, starts the managers.
    /// </summary>
    /// <remarks>Needs <see cref="ArenaScene"/> in the build's scene list (tests/PlayModeHost has it); skipped otherwise.</remarks>
    public class SceneFlowTests : NetSessionTest
    {
        /// <summary>A scene in tests/PlayModeHost's build list holding one GameObject, <c>NetFlowArenaMarker</c>.</summary>
        public const string ArenaScene = "NetFlowArena";

        private NetSession _host;
        private NetSession _a;
        private NetSession _b;

        public SceneFlowTests() : base(Topology.Host, clients: 2)
        {
        }

        protected override bool SceneManagement => true;

        protected override string GameVersion => "1";

        protected override IEnumerator OnSetup()
        {
            if (SceneUtility.GetBuildIndexByScenePath(ArenaScene) < 0)
            {
                Assert.Ignore("Scene '" + ArenaScene + "' is not in this project's build list (tests/PlayModeHost has it).");
            }

            yield return null;
        }

        protected override void OnSessionsCreated()
        {
            _host = Server;
            _a = Clients[0];
            _b = Clients[1];
            _host.ProtocolHash = _a.ProtocolHash = _b.ProtocolHash = new ProtocolHash(0x20);
        }

        protected override IEnumerator OnSessionsTearDown()
        {
            _a = _b = _host = null;
            yield return null;
        }

        private IEnumerator StartHost()
        {
            Assert.That(_host.StartHost(Config()), Is.True, "StartHost");
            NetcodeIntegrationTestHelpers.RegisterHandlers(m_ServerNetworkManager, true);
            yield return WaitUntil(() => _host.State == SessionState.Running, "the host never reached Running");
        }

        private IEnumerator Join(NetSession client, NetworkManager manager)
        {
            Assert.That(client.JoinByAddress(NetConfig.LoopbackAddress, Port, Config()), Is.True, "JoinByAddress");
            NetcodeIntegrationTestHelpers.RegisterHandlers(manager);
            yield return WaitUntil(() => client.State == SessionState.Connected, () => "the client never connected (" + client.State + ", " + client.LastDisconnect + ")");
        }

        private static int ArenaCopies()
        {
            int copies = 0;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.name == ArenaScene && scene.isLoaded)
                {
                    copies++;
                }
            }

            return copies;
        }

        [UnityTest]
        public IEnumerator ServerLoadsAScene_EveryClientFollows_ThenItUnloadsEverywhere()
        {
            yield return StartHost();
            yield return Join(_a, m_ClientNetworkManagers[0]);
            yield return Join(_b, m_ClientNetworkManagers[1]);

            var started = new List<string>();
            var clientLoaded = new List<ulong>();
            var hostLoaded = new List<SceneTransition>();
            var aLoaded = new List<SceneTransition>();
            var hostUnloaded = new List<SceneTransition>();
            var bUnloaded = new List<SceneTransition>();
            _host.Scenes.LoadStarted += (scene, mode) => started.Add(scene);
            _a.Scenes.LoadStarted += (scene, mode) => started.Add("a:" + scene);
            _host.Scenes.ClientLoaded += (client, scene) => clientLoaded.Add(client);
            _host.Scenes.Loaded += t => hostLoaded.Add(t);
            _a.Scenes.Loaded += t => aLoaded.Add(t);
            _host.Scenes.Unloaded += t => hostUnloaded.Add(t);
            _b.Scenes.Unloaded += t => bUnloaded.Add(t);

            Assert.That(_host.Scenes.Load(ArenaScene, NetSceneMode.Additive), Is.EqualTo(SceneRequestResult.Started));
            Assert.That(_host.Scenes.IsBusy, Is.True);
            Assert.That(_host.Scenes.PendingScene, Is.EqualTo(ArenaScene));
            Assert.That(_host.Scenes.Load(ArenaScene, NetSceneMode.Additive), Is.EqualTo(SceneRequestResult.Busy), "one change at a time");

            yield return WaitUntil(() => hostLoaded.Count == 1 && aLoaded.Count == 1, () => "the load never completed everywhere (host " + hostLoaded.Count + ", Ann " + aLoaded.Count + ")");
            SceneTransition done = hostLoaded[0];
            Assert.That(done.SceneName, Is.EqualTo(ArenaScene));
            Assert.That(done.Mode, Is.EqualTo(NetSceneMode.Additive));
            Assert.That(done.Completed, Is.EquivalentTo(new[] { NetworkManager.ServerClientId, _a.LocalClientId, _b.LocalClientId }));
            Assert.That(done.TimedOut, Is.Empty);
            Assert.That(aLoaded[0].SceneName, Is.EqualTo(ArenaScene), "clients see the completion too");
            Assert.That(started, Does.Contain(ArenaScene).And.Contain("a:" + ArenaScene));
            Assert.That(clientLoaded, Is.SupersetOf(new[] { _a.LocalClientId, _b.LocalClientId }));
            Assert.That(_host.Scenes.IsBusy, Is.False);
            Assert.That(ArenaCopies(), Is.GreaterThanOrEqualTo(1));
            Assert.That(GameObject.Find("NetFlowArenaMarker"), Is.Not.Null, "the scene's contents are there");

            Assert.That(_host.Scenes.Unload(ArenaScene), Is.EqualTo(SceneRequestResult.Started));
            yield return WaitUntil(() => hostUnloaded.Count == 1 && bUnloaded.Count == 1, "the unload never completed everywhere");
            Assert.That(hostUnloaded[0].SceneName, Is.EqualTo(ArenaScene));
            Assert.That(_host.Scenes.IsBusy, Is.False);
            yield return WaitUntil(() => ArenaCopies() == 0, "a copy of the scene is still loaded");
            Assert.That(_host.Scenes.Unload(ArenaScene), Is.EqualTo(SceneRequestResult.NotLoaded));
        }

        [UnityTest]
        public IEnumerator AClientJoiningLater_IsSynchronisedToTheServersScenes()
        {
            yield return StartHost();
            yield return Join(_a, m_ClientNetworkManagers[0]);
            var loaded = new List<SceneTransition>();
            _host.Scenes.Loaded += t => loaded.Add(t);
            Assert.That(_host.Scenes.Load(ArenaScene, NetSceneMode.Additive), Is.EqualTo(SceneRequestResult.Started));
            yield return WaitUntil(() => loaded.Count == 1, "the load never completed");

            var synchronized = new List<ulong>();
            _host.Scenes.ClientSynchronized += id => synchronized.Add(id);
            yield return Join(_b, m_ClientNetworkManagers[1]);

            yield return WaitUntil(() => synchronized.Contains(_b.LocalClientId), "Bob was never synchronised to the server's scenes");
            Assert.That(synchronized, Has.No.Member(_a.LocalClientId), "Ann was already in sync");
        }

        [UnityTest]
        public IEnumerator OnlyARunningServerChangesScenes()
        {
            Assert.That(_host.Scenes.Load(ArenaScene), Is.EqualTo(SceneRequestResult.NotRunning));
            Assert.Throws<System.ArgumentException>(() => _host.Scenes.Load(" "));

            yield return StartHost();
            yield return Join(_a, m_ClientNetworkManagers[0]);

            Assert.That(_a.Scenes.Load(ArenaScene, NetSceneMode.Additive), Is.EqualTo(SceneRequestResult.NotServer), "clients follow; they never lead");
            Assert.That(_a.Scenes.Unload(ArenaScene), Is.EqualTo(SceneRequestResult.NotServer));
            Assert.That(_host.Scenes.Load("NoSuchScene", NetSceneMode.Additive), Is.EqualTo(SceneRequestResult.UnknownScene));
            Assert.That(_host.Scenes.Unload(ArenaScene), Is.EqualTo(SceneRequestResult.NotLoaded));
            Assert.That(_host.Scenes.IsEnabled, Is.True);
        }

        [UnityTest]
        public IEnumerator WithSceneManagementOff_RequestsSayWhy()
        {
            foreach (NetworkManager manager in m_NetworkManagers)
            {
                manager.NetworkConfig.EnableSceneManagement = false;
            }

            Assert.That(_host.StartHost(Config()), Is.True);
            yield return WaitUntil(() => _host.State == SessionState.Running, "the host never reached Running");

            Assert.That(_host.Scenes.IsEnabled, Is.False);
            Assert.That(_host.Scenes.Load(ArenaScene), Is.EqualTo(SceneRequestResult.SceneManagementDisabled));
        }
    }
}
