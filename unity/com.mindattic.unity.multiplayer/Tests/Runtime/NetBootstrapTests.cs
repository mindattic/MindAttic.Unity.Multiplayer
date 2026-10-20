using System.Collections;
using MindAttic.Unity.Multiplayer.Core;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;

namespace MindAttic.Unity.Multiplayer.Tests
{
    /// <summary>
    /// UM-T8: the headless bootstrap. Run in batch mode (tests/run-playmode.ps1 passes -batchmode -nographics),
    /// a scene with a NetBootstrap starts a dedicated server by itself and paces the process to the tick rate.
    /// </summary>
    public class NetBootstrapTests
    {
        private GameObject _go;
        private int _frameRate;
        private int _vSync;

        [SetUp]
        public void SetUp()
        {
            _frameRate = Application.targetFrameRate;
            _vSync = QualitySettings.vSyncCount;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_go != null)
            {
                Object.Destroy(_go);
                _go = null;
            }

            yield return null;
            Application.targetFrameRate = _frameRate;
            QualitySettings.vSyncCount = _vSync;
        }

        [UnityTest]
        public IEnumerator Headless_StartsADedicatedServerPacedToTheTickRate()
        {
            if (!HeadlessBootstrap.IsHeadless)
            {
                Assert.Ignore("Needs a headless editor (-batchmode / -nographics): run tests/run-playmode.ps1.");
            }

            _go = new GameObject("NetBootstrap test");
            _go.SetActive(false);
            NetworkManager manager = _go.AddComponent<NetworkManager>();
            _go.AddComponent<UnityTransport>();
            NetBootstrap bootstrap = _go.AddComponent<NetBootstrap>();
            bootstrap.SetDefaults(new NetConfig { Topology = Topology.Host, Port = 47821, TickRate = 24, GameVersion = "test" });
            if (manager.NetworkConfig == null)
            {
                manager.NetworkConfig = new NetworkConfig();
            }

            manager.NetworkConfig.EnableSceneManagement = false;
            _go.SetActive(true);

            Assert.That(bootstrap.Session, Is.Not.Null, "Awake creates the session");
            Assert.That(NetBootstrap.Instance, Is.SameAs(bootstrap));

            float deadline = Time.realtimeSinceStartup + 5f;
            while (bootstrap.Session.State != SessionState.Running && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(bootstrap.Session.State, Is.EqualTo(SessionState.Running), "a headless process serves by itself");
            Assert.That(bootstrap.Session.Topology, Is.EqualTo(Topology.Server), "headless means dedicated server, not the configured Host");
            Assert.That(bootstrap.Session.IsHost, Is.False);
            Assert.That(bootstrap.Session.Clock.TickRate, Is.EqualTo(24));
            Assert.That(Application.targetFrameRate, Is.EqualTo(24), "targetFrameRate = tick rate");
            Assert.That(QualitySettings.vSyncCount, Is.EqualTo(0));
            Assert.That(Application.runInBackground, Is.True);

            NetSession session = bootstrap.Session;
            Object.Destroy(_go);
            _go = null;
            yield return null;
            yield return null;

            Assert.That(session.State, Is.EqualTo(SessionState.Idle), "destroying the bootstrap stops its session");
            Assert.That(NetBootstrap.Instance, Is.Null);
        }

        [Test]
        public void HeadlessApply_RejectsAnOutOfRangeTickRate()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => HeadlessBootstrap.Apply(0));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => HeadlessBootstrap.Apply(NetConfig.MaxTickRate + 1));
        }
    }
}
