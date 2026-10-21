using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Multiplayer.Core;
using NUnit.Framework;
using Unity.Netcode.TestHelpers.Runtime;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.Multiplayer.Tests
{
    /// <summary>
    /// UM-T13: join codes end to end against an in-process rendezvous server (<see cref="InProcessRendezvousServer"/>).
    /// A dedicated server registers itself and heartbeats, a client joins with the code as a player would type it, the
    /// code is withdrawn when the server leaves, RunPod's mapped port is what gets registered, and a listen server
    /// defaults to Relay.
    /// </summary>
    [TestFixture(TransportKind.Udp, (ushort)47901)]
    [TestFixture(TransportKind.WebSocket, (ushort)47902)]
    public class JoinByCodeTests : NetcodeIntegrationTest
    {
        private readonly TransportKind _transport;
        private readonly ushort _port;
        private NetSession _server;
        private NetSession _client;
        private InProcessRendezvousServer _rendezvous;
        private readonly List<string> _serverFailures = new List<string>();
        private readonly List<string> _clientFailures = new List<string>();

        public JoinByCodeTests(TransportKind transport, ushort port) : base(HostOrServer.Server)
        {
            _transport = transport;
            _port = port;
        }

        protected override int NumberOfClients => 1;

        protected override bool CanStartServerAndClients() => false;

        protected override void OnServerAndClientsCreated()
        {
            NetSessionTestSupport.PrepareManagers(m_NetworkManagers);
            _rendezvous = new InProcessRendezvousServer(ttlSeconds: 1);
            _server = new NetSession(m_ServerNetworkManager) { EnvironmentReader = _ => null };
            _client = new NetSession(m_ClientNetworkManagers[0]);
            _serverFailures.Clear();
            _clientFailures.Clear();
            _server.JoinCodeFailed += _serverFailures.Add;
            _client.JoinCodeFailed += _clientFailures.Add;
            base.OnServerAndClientsCreated();
        }

        protected override IEnumerator OnTearDown()
        {
            _client?.Dispose();
            _server?.Dispose();
            _rendezvous?.Dispose();
            _client = null;
            _server = null;
            _rendezvous = null;
            yield return null;
        }

        private NetConfig ServerConfig()
        {
            return new NetConfig
            {
                Port = _port,
                Transport = _transport,
                GameVersion = "test",
                RendezvousUrl = _rendezvous.Url,
                RendezvousSecret = InProcessRendezvousServer.Secret,
            };
        }

        /// <summary>A client knows only the rendezvous URL; the transport comes from the registration.</summary>
        private NetConfig ClientConfig()
        {
            return new NetConfig { GameVersion = "test", RendezvousUrl = _rendezvous.Url };
        }

        private static IEnumerator Await(Task task, float seconds = 10f)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (!task.IsCompleted)
            {
                if (Time.realtimeSinceStartup > until)
                {
                    Assert.Fail("Timed out waiting for a task.");
                }

                yield return null;
            }

            if (task.IsFaulted)
            {
                throw task.Exception.GetBaseException();
            }
        }

        private IEnumerator StartDedicatedServerAndGetCode()
        {
            Assert.That(_server.StartServer(ServerConfig()), Is.True, "StartServer");
            yield return WaitForConditionOrTimeOut(() => !_server.PublishedCode.IsEmpty || _serverFailures.Count > 0);
            AssertOnTimeout("the dedicated server never registered (state " + _server.State + ")");
            Assert.That(_serverFailures, Is.Empty, string.Join(" | ", _serverFailures));
        }

        [UnityTest]
        public IEnumerator DedicatedServer_SelfRegisters_ClientJoinsByTypedCode_CodeWithdrawnOnLeave()
        {
            var published = new List<JoinCode>();
            _server.JoinCodePublished += published.Add;
            yield return StartDedicatedServerAndGetCode();

            JoinCode code = _server.PublishedCode;
            Assert.That(published, Is.EqualTo(new[] { code }), "JoinCodePublished raised once with the code");
            Assert.That(_server.PublishedCodeText, Is.EqualTo(JoinCodeFormat.Format(code.Value)));
            InProcessRendezvousServer.Entry entry = _rendezvous.Find(code);
            Assert.That(entry, Is.Not.Null, "the rendezvous service knows the code");
            Assert.That(entry.Address, Is.EqualTo(NetConfig.LoopbackAddress), "no public address given: the service used the caller's");
            Assert.That(entry.Port, Is.EqualTo(_port));
            Assert.That(entry.Transport, Is.EqualTo(_transport));

            // Heartbeats keep it alive past its 1 s TTL.
            yield return WaitForConditionOrTimeOut(() => _rendezvous.Heartbeats >= 3);
            AssertOnTimeout("the server never sent heartbeats");
            Assert.That(_rendezvous.Find(code), Is.Not.Null, "still registered after several TTLs");

            // The player types it lower case with spaces.
            string typed = _server.PublishedCodeText.ToLowerInvariant().Replace("-", " ");
            Task<bool> join = _client.JoinByCode(typed, ClientConfig());
            yield return Await(join);
            Assert.That(join.Result, Is.True, "JoinByCode: " + string.Join(" | ", _clientFailures));
            yield return WaitForConditionOrTimeOut(() => _client.State == SessionState.Connected && _server.ConnectedClientIds.Count == 1);
            AssertOnTimeout("the client never connected by code (client " + _client.State + ")");
            Assert.That(_client.Config.Transport, Is.EqualTo(_transport), "the transport came from the registration");
            Assert.That(_client.Config.Port, Is.EqualTo(_port));

            _client.Leave();
            _server.Leave();
            yield return WaitForConditionOrTimeOut(() => _server.State == SessionState.Idle && _rendezvous.Count == 0);
            AssertOnTimeout("the code was not withdrawn when the server left (" + _rendezvous.Count + " registrations)");
            Assert.That(_server.PublishedCode.IsEmpty, Is.True);

            // The old code now leads nowhere.
            yield return WaitForConditionOrTimeOut(() => _client.CanStart);
            Task<bool> stale = _client.JoinByCode(code.Value, ClientConfig());
            yield return Await(stale);
            Assert.That(stale.Result, Is.False);
            Assert.That(_clientFailures, Has.Some.Contains("No game is using code"));
            Assert.That(_client.State, Is.Not.EqualTo(SessionState.Connecting));
        }

        [UnityTest]
        public IEnumerator ALostRegistration_IsRegisteredAgainWithANewCode()
        {
            yield return StartDedicatedServerAndGetCode();
            JoinCode first = _server.PublishedCode;

            _rendezvous.ForgetAll(); // the service restarted
            yield return WaitForConditionOrTimeOut(() => !_server.PublishedCode.IsEmpty && _server.PublishedCode != first);
            AssertOnTimeout("the server did not register again after its registration was lost");
            Assert.That(_rendezvous.Find(_server.PublishedCode), Is.Not.Null);
            Assert.That(_server.State, Is.EqualTo(SessionState.Running), "losing a registration does not stop the server");
        }

        [UnityTest]
        public IEnumerator RunPod_RegistersTheMappedTcpPortAndPublicIp()
        {
            var env = new Dictionary<string, string>
            {
                { "RUNPOD_TCP_PORT_" + _port, "40999" },
                { "RUNPOD_PUBLIC_IP", "203.0.113.50" },
            };
            _server.EnvironmentReader = name => env.TryGetValue(name, out string value) ? value : null;
            if (_transport == TransportKind.Udp)
            {
                // RunPod maps TCP only; the server warns and registers its own UDP port.
                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("RunPod maps port .* over TCP only"));
            }

            yield return StartDedicatedServerAndGetCode();
            InProcessRendezvousServer.Entry entry = _rendezvous.Find(_server.PublishedCode);
            Assert.That(entry, Is.Not.Null);
            if (_transport == TransportKind.WebSocket)
            {
                Assert.That(entry.Port, Is.EqualTo(40999), "the mapped port, not the pod-internal one");
                Assert.That(entry.Address, Is.EqualTo("203.0.113.50"));
            }
            else
            {
                Assert.That(entry.Port, Is.EqualTo(_port));
                Assert.That(entry.Address, Is.EqualTo(NetConfig.LoopbackAddress));
            }
        }

        [UnityTest]
        public IEnumerator WrongSecret_ReportsTheFailureAndKeepsServing()
        {
            NetConfig config = ServerConfig();
            config.RendezvousSecret = "wrong";
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Could not register this server.*registration secret"));
            Assert.That(_server.StartServer(config), Is.True);
            yield return WaitForConditionOrTimeOut(() => _serverFailures.Count > 0);
            AssertOnTimeout("no JoinCodeFailed for a wrong secret");
            Assert.That(_serverFailures[0], Does.Contain("Wrong or missing registration secret."));
            Assert.That(_server.State, Is.EqualTo(SessionState.Running));
            Assert.That(_server.PublishedCode.IsEmpty, Is.True);
        }

        [UnityTest]
        public IEnumerator ListenServer_HostWithCode_RendezvousPublishesTheHostsAddress()
        {
            NetConfig config = ServerConfig();
            config.JoinCodes = JoinCodeProvider.Rendezvous;
            config.PublicAddress = NetConfig.LoopbackAddress;
            Task<JoinCode> host = _server.HostWithCode(config);
            yield return Await(host);
            Assert.That(host.Result.IsEmpty, Is.False, string.Join(" | ", _serverFailures));
            Assert.That(_server.IsHost, Is.True);

            Task<bool> join = _client.JoinByCode(JoinCodeFormat.Format(host.Result.Value), ClientConfig());
            yield return Await(join);
            Assert.That(join.Result, Is.True, string.Join(" | ", _clientFailures));
            yield return WaitForConditionOrTimeOut(() => _client.State == SessionState.Connected);
            AssertOnTimeout("the client never connected to the host by code");
        }

        [UnityTest]
        public IEnumerator ListenServer_DefaultsToRelay()
        {
            Assert.That(JoinCodeDefaults.ForPublishing(Topology.Host, ServerConfig().JoinCodes), Is.EqualTo(JoinCodeProvider.Relay));
            if (RelayJoinCodeService.IsAvailable)
            {
                Assert.Ignore("com.unity.services.multiplayer is installed; a real Relay allocation needs a linked Cloud project.");
            }

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("com.unity.services.multiplayer"));
            Task<JoinCode> host = _server.HostWithCode(ServerConfig());
            yield return Await(host);
            Assert.That(host.Result.IsEmpty, Is.True);
            Assert.That(_serverFailures, Has.Some.Contains("com.unity.services.multiplayer"), "Relay was chosen and its absence reported");
            Assert.That(_server.IsActive, Is.False, "no host is started without its Relay allocation");
            Assert.That(_rendezvous.Count, Is.EqualTo(0), "the rendezvous service is not used for a listen server by default");
        }

        [UnityTest]
        public IEnumerator BadOrUnknownCodes_FailWithAReason()
        {
            Task<bool> typo = _client.JoinByCode("not a code!", ClientConfig());
            yield return Await(typo);
            Assert.That(typo.Result, Is.False);

            Task<bool> unknown = _client.JoinByCode(JoinCode.Generate().Value, ClientConfig());
            yield return Await(unknown);
            Assert.That(unknown.Result, Is.False);
            Assert.That(_clientFailures, Has.Count.EqualTo(2), string.Join(" | ", _clientFailures));
            Assert.That(_clientFailures[1], Does.Contain("No game is using code"));

            Task<bool> noService = _client.JoinByCode(JoinCode.Generate().Value, new NetConfig { GameVersion = "test", JoinCodes = JoinCodeProvider.Rendezvous });
            yield return Await(noService);
            Assert.That(noService.Result, Is.False);
            Assert.That(_clientFailures[2], Does.Contain("RendezvousUrl"));
            Assert.That(_client.State, Is.EqualTo(SessionState.Idle));
        }

        [UnityTest]
        public IEnumerator UnreachableRendezvous_FailsTheJoinWithAReason()
        {
            var config = new NetConfig { GameVersion = "test", RendezvousUrl = "http://127.0.0.1:1" };
            Task<bool> join = _client.JoinByCode(JoinCode.Generate().Value, config);
            yield return Await(join, 15f);
            Assert.That(join.Result, Is.False);
            Assert.That(_clientFailures, Has.Some.Contains("Could not look up code"));
        }
    }
}
