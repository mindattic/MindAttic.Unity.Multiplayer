using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MindAttic.Unity.Multiplayer.Core;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.TestHelpers.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MindAttic.Unity.Multiplayer.Tests
{
    /// <summary>
    /// UM-T10: server-driven scene flow through <see cref="NetSceneFlow"/>, over a real Unity Transport on loopback. A
    /// Host and two Clients with NGO scene management on. In one process the clients' loads are simulated by NGO's
    /// integration-test scene handler, registered by hand because NetSession, not the harness, starts the managers.
    /// </summary>
    /// <remarks>Needs <see cref="ArenaScene"/> in the build's scene list (tests/PlayModeHost has it); skipped otherwise.</remarks>
    public class SceneFlowTests : NetcodeIntegrationTest
    {
        /// <summary>A scene in tests/PlayModeHost's build list holding one GameObject, <c>NetFlowArenaMarker</c>.</summary>
        public const string ArenaScene = "NetFlowArena";

        private const ushort Port = 47881;

        private NetSession _host;
        private NetSession _a;
        private NetSession _b;

        public SceneFlowTests() : base(HostOrServer.Host)
        {
        }

        protected override int NumberOfClients => 2;

        protected override bool CanStartServerAndClients() => false;

        protected override IEnumerator OnSetup()
        {
            if (SceneUtility.GetBuildIndexByScenePath(ArenaScene) < 0)
            {
                Assert.Ignore("Scene '" + ArenaScene + "' is not in this project's build list (tests/PlayModeHost has it).");
            }

            yield return null;
        }

        protected override void OnServerAndClientsCreated()
        {
            foreach (NetworkManager manager in m_NetworkManagers)
            {
                manager.NetworkConfig.EnableSceneManagement = true;
            }

            _host = new NetSession(m_ServerNetworkManager) { ProtocolHash = new ProtocolHash(0x20) };
            _a = new NetSession(m_ClientNetworkManagers[0]) { ProtocolHash = new ProtocolHash(0x20) };
            _b = new NetSession(m_ClientNetworkManagers[1]) { ProtocolHash = new ProtocolHash(0x20) };
            base.OnServerAndClientsCreated();
        }

        protected override IEnumerator OnTearDown()
        {
            _a?.Dispose();
            _b?.Dispose();
            _host?.Dispose();
            _a = _b = _host = null;
            yield return null;
        }

        private static NetConfig Config()
        {
            return new NetConfig { Port = Port, GameVersion = "1" };
        }

        private IEnumerator StartHost()
        {
            Assert.That(_host.StartHost(Config()), Is.True, "StartHost");
            NetcodeIntegrationTestHelpers.RegisterHandlers(m_ServerNetworkManager, true);
            yield return WaitForConditionOrTimeOut(() => _host.State == SessionState.Running);
            AssertOnTimeout("the host never reached Running");
        }

        private IEnumerator Join(NetSession client, NetworkManager manager)
        {
            Assert.That(client.JoinByAddress(NetConfig.LoopbackAddress, Port, Config()), Is.True, "JoinByAddress");
            NetcodeIntegrationTestHelpers.RegisterHandlers(manager);
            yield return WaitForConditionOrTimeOut(() => client.State == SessionState.Connected);
            AssertOnTimeout("the client never connected (" + client.State + ", " + client.LastDisconnect + ")");
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

            yield return WaitForConditionOrTimeOut(() => hostLoaded.Count == 1 && aLoaded.Count == 1);
            AssertOnTimeout("the load never completed everywhere (host " + hostLoaded.Count + ", Ann " + aLoaded.Count + ")");
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
            yield return WaitForConditionOrTimeOut(() => hostUnloaded.Count == 1 && bUnloaded.Count == 1);
            AssertOnTimeout("the unload never completed everywhere");
            Assert.That(hostUnloaded[0].SceneName, Is.EqualTo(ArenaScene));
            Assert.That(_host.Scenes.IsBusy, Is.False);
            yield return WaitForConditionOrTimeOut(() => ArenaCopies() == 0);
            AssertOnTimeout("a copy of the scene is still loaded");
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
            yield return WaitForConditionOrTimeOut(() => loaded.Count == 1);
            AssertOnTimeout("the load never completed");

            var synchronized = new List<ulong>();
            _host.Scenes.ClientSynchronized += id => synchronized.Add(id);
            yield return Join(_b, m_ClientNetworkManagers[1]);

            yield return WaitForConditionOrTimeOut(() => synchronized.Contains(_b.LocalClientId));
            AssertOnTimeout("Bob was never synchronised to the server's scenes");
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
            yield return WaitForConditionOrTimeOut(() => _host.State == SessionState.Running);
            AssertOnTimeout("the host never reached Running");

            Assert.That(_host.Scenes.IsEnabled, Is.False);
            Assert.That(_host.Scenes.Load(ArenaScene), Is.EqualTo(SceneRequestResult.SceneManagementDisabled));
        }
    }
}
