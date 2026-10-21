using System.Collections;
using Unity.Multiplayer.Core;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.Multiplayer.Tests
{
    /// <summary>
    /// UM-T8: the headless bootstrap. Run in batch mode (tests/run-playmode.ps1 passes -batchmode -nographics),
    /// a scene with a NetBootstrap starts a dedicated server by itself and paces the process to the tick rate.
    /// UM-T33: <see cref="NetBootstrap.Configure(NetConfig)"/> sets its defaults in code, validated, refused while the
    /// session runs, and still overridden by the command line.
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
            bootstrap.Configure(new NetConfig { Topology = Topology.Host, Port = 47821, TickRate = 24, GameVersion = "test" });
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

        private NetBootstrap NewInactiveBootstrap()
        {
            _go = new GameObject("NetBootstrap config test");
            _go.SetActive(false);
            NetworkManager manager = _go.AddComponent<NetworkManager>();
            _go.AddComponent<UnityTransport>();
            if (manager.NetworkConfig == null)
            {
                manager.NetworkConfig = new NetworkConfig();
            }

            manager.NetworkConfig.EnableSceneManagement = false;
            return _go.AddComponent<NetBootstrap>();
        }

        private static NetConfig EveryField()
        {
            return new NetConfig
            {
                Topology = Topology.Client,
                Address = "10.1.2.3",
                Port = 47830,
                Transport = TransportKind.WebSocket,
                TickRate = 20,
                MaxPlayers = 3,
                ServerName = "UM-T33",
                Password = "hunter2",
                GameVersion = "um-t33",
                ReconnectGraceSeconds = 12,
                JoinCodes = JoinCodeProvider.Rendezvous,
                RendezvousUrl = "https://rendezvous.example.com",
                RendezvousSecret = "s3cret",
                PublicAddress = "203.0.113.7",
                PublicPort = 40000,
                Conditions = NetConditionsProfile.Custom,
                SimulatedLatencyMs = 80,
                SimulatedJitterMs = 5,
                SimulatedPacketLossPercent = 4,
            };
        }

        private static void AssertSame(NetConfig actual, NetConfig expected)
        {
            Assert.That(actual.Topology, Is.EqualTo(expected.Topology));
            Assert.That(actual.Address, Is.EqualTo(expected.Address));
            Assert.That(actual.Port, Is.EqualTo(expected.Port));
            Assert.That(actual.Transport, Is.EqualTo(expected.Transport));
            Assert.That(actual.TickRate, Is.EqualTo(expected.TickRate));
            Assert.That(actual.MaxPlayers, Is.EqualTo(expected.MaxPlayers));
            Assert.That(actual.ServerName, Is.EqualTo(expected.ServerName));
            Assert.That(actual.Password, Is.EqualTo(expected.Password));
            Assert.That(actual.GameVersion, Is.EqualTo(expected.GameVersion));
            Assert.That(actual.ReconnectGraceSeconds, Is.EqualTo(expected.ReconnectGraceSeconds));
            Assert.That(actual.JoinCodes, Is.EqualTo(expected.JoinCodes));
            Assert.That(actual.RendezvousUrl, Is.EqualTo(expected.RendezvousUrl));
            Assert.That(actual.RendezvousSecret, Is.EqualTo(expected.RendezvousSecret));
            Assert.That(actual.PublicAddress, Is.EqualTo(expected.PublicAddress));
            Assert.That(actual.PublicPort, Is.EqualTo(expected.PublicPort));
            Assert.That(actual.Conditions, Is.EqualTo(expected.Conditions));
            Assert.That(actual.SimulatedLatencyMs, Is.EqualTo(expected.SimulatedLatencyMs));
            Assert.That(actual.SimulatedJitterMs, Is.EqualTo(expected.SimulatedJitterMs));
            Assert.That(actual.SimulatedPacketLossPercent, Is.EqualTo(expected.SimulatedPacketLossPercent));
        }

        [Test]
        public void Configure_SetsEveryDefaultAndCopiesTheConfig()
        {
            NetBootstrap bootstrap = NewInactiveBootstrap();
            NetConfig config = EveryField();

            Assert.That(bootstrap.Configure(config), Is.SameAs(bootstrap), "Configure chains");
            config.Port = 1;
            config.Topology = Topology.Host;

            AssertSame(bootstrap.DefaultConfig, EveryField());
            AssertSame(bootstrap.BuildConfig(new string[0]), EveryField());
            Assert.That(bootstrap.DefaultConfig, Is.Not.SameAs(bootstrap.DefaultConfig), "a new copy every read");
        }

        [Test]
        public void ConfigureEdit_ChangesOnlyWhatItSets()
        {
            NetBootstrap bootstrap = NewInactiveBootstrap();
            bootstrap.Configure(EveryField());

            bootstrap.Configure(c => { c.Topology = Topology.Server; c.Port = 47831; });

            NetConfig expected = EveryField();
            expected.Topology = Topology.Server;
            expected.Port = 47831;
            AssertSame(bootstrap.DefaultConfig, expected);
        }

        [Test]
        public void Configure_RefusesAnInvalidConfigAndChangesNothing()
        {
            NetBootstrap bootstrap = NewInactiveBootstrap();
            bootstrap.Configure(EveryField());

            Assert.Throws<System.ArgumentNullException>(() => bootstrap.Configure((NetConfig)null));
            Assert.Throws<System.ArgumentNullException>(() => bootstrap.Configure((System.Action<NetConfig>)null));
            var bad = Assert.Throws<System.ArgumentException>(
                () => bootstrap.Configure(new NetConfig { Port = 0, TickRate = 0, GameVersion = " " }));
            Assert.That(bad.Message, Does.Contain("Port").And.Contain("TickRate").And.Contain("GameVersion"));
            Assert.Throws<System.ArgumentException>(() => bootstrap.Configure(c => c.MaxPlayers = 0));

            AssertSame(bootstrap.DefaultConfig, EveryField());
        }

        [Test]
        public void CommandLine_WinsOverConfigure()
        {
            NetBootstrap bootstrap = NewInactiveBootstrap();
            bootstrap.Configure(EveryField());

            NetConfig built = bootstrap.BuildConfig(new[] { "Game.exe", "-batchmode", "-port", "47999", "-transport", "udp", "-mode", "server", "-tickrate", "60" });

            Assert.That(built.Port, Is.EqualTo(47999));
            Assert.That(built.Transport, Is.EqualTo(TransportKind.Udp));
            Assert.That(built.Topology, Is.EqualTo(Topology.Server));
            Assert.That(built.TickRate, Is.EqualTo(60));
            Assert.That(built.MaxPlayers, Is.EqualTo(3), "what the command line leaves alone keeps the code default");
            Assert.That(built.GameVersion, Is.EqualTo("um-t33"));
            Assert.That(bootstrap.DefaultConfig.Port, Is.EqualTo(47830), "the command line never changes the defaults");
        }

        [UnityTest]
        public IEnumerator Configure_IsRefusedWhileTheSessionRuns()
        {
            NetBootstrap bootstrap = NewInactiveBootstrap();
            bootstrap.Configure(new NetConfig { Topology = Topology.Server, Port = 47832, GameVersion = "um-t33" });
            _go.SetActive(true);
            yield return null; // a headless editor serves by itself from Start
            if (!bootstrap.Session.IsActive)
            {
                Assert.That(bootstrap.StartServer(), Is.True);
            }

            float deadline = Time.realtimeSinceStartup + 5f;
            while (bootstrap.Session.State != SessionState.Running && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(bootstrap.Session.State, Is.EqualTo(SessionState.Running));
            Assert.That(bootstrap.Session.Topology, Is.EqualTo(Topology.Server));
            var refused = Assert.Throws<System.InvalidOperationException>(() => bootstrap.Configure(c => c.Port = 47833));
            Assert.That(refused.Message, Does.Contain("Running"));
            Assert.That(bootstrap.DefaultConfig.Port, Is.EqualTo(47832), "a refused change changes nothing");

            Assert.That(bootstrap.Leave(), Is.True);
            deadline = Time.realtimeSinceStartup + 5f;
            while (bootstrap.Session.IsActive && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(bootstrap.Session.IsActive, Is.False);
            bootstrap.Configure(c => c.Port = 47833);
            Assert.That(bootstrap.DefaultConfig.Port, Is.EqualTo(47833), "allowed again once the session ended");
        }

        [Test]
        public void HeadlessApply_RejectsAnOutOfRangeTickRate()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => HeadlessBootstrap.Apply(0));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => HeadlessBootstrap.Apply(NetConfig.MaxTickRate + 1));
        }
    }
}
