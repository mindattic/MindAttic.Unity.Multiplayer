using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Multiplayer.Core;
using NUnit.Framework;
using Unity.Multiplayer.Testing;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.Multiplayer.Tests
{
    /// <summary>
    /// UM-T13: join codes end to end against the test kit's in-process rendezvous server (<see cref="InProcessRendezvousServer"/>).
    /// A dedicated server registers itself and heartbeats, a client joins with the code as a player would type it, the
    /// code is withdrawn when the server leaves, RunPod's mapped port is what gets registered, and a listen server
    /// defaults to Relay.
    /// </summary>
    [TestFixture(TransportKind.Udp)]
    [TestFixture(TransportKind.WebSocket)]
    public class JoinByCodeTests : NetSessionTest
    {
        private InProcessRendezvousServer _rendezvous;
        private readonly List<string> _serverFailures = new List<string>();
        private readonly List<string> _clientFailures = new List<string>();

        public JoinByCodeTests(TransportKind transport) : base(Topology.Server, clients: 1, transport: transport)
        {
        }

        protected override void OnSessionsCreated()
        {
            _rendezvous = StartRendezvous(ttlSeconds: 1);
            Server.EnvironmentReader = _ => null;
            _serverFailures.Clear();
            _clientFailures.Clear();
            Server.JoinCodeFailed += _serverFailures.Add;
            Client.JoinCodeFailed += _clientFailures.Add;
        }

        private NetConfig ServerConfig()
        {
            return _rendezvous.ForServer(Config());
        }

        /// <summary>A client knows only the rendezvous URL; the transport comes from the registration.</summary>
        private NetConfig ClientConfig()
        {
            return _rendezvous.ForClient(new NetConfig { GameVersion = GameVersion });
        }

        private IEnumerator StartDedicatedServerAndGetCode()
        {
            Assert.That(Server.StartServer(ServerConfig()), Is.True, "StartServer");
            yield return WaitUntil(() => !Server.PublishedCode.IsEmpty || _serverFailures.Count > 0, () => "the dedicated server never registered (state " + Server.State + ")");
            Assert.That(_serverFailures, Is.Empty, string.Join(" | ", _serverFailures));
        }

        [UnityTest]
        public IEnumerator DedicatedServer_SelfRegisters_ClientJoinsByTypedCode_CodeWithdrawnOnLeave()
        {
            var published = new List<JoinCode>();
            Server.JoinCodePublished += published.Add;
            yield return StartDedicatedServerAndGetCode();

            JoinCode code = Server.PublishedCode;
            Assert.That(published, Is.EqualTo(new[] { code }), "JoinCodePublished raised once with the code");
            Assert.That(Server.PublishedCodeText, Is.EqualTo(JoinCodeFormat.Format(code.Value)));
            InProcessRendezvousServer.Entry entry = _rendezvous.Find(code);
            Assert.That(entry, Is.Not.Null, "the rendezvous service knows the code");
            Assert.That(entry.Address, Is.EqualTo(NetConfig.LoopbackAddress), "no public address given: the service used the caller's");
            Assert.That(entry.Port, Is.EqualTo(Port));
            Assert.That(entry.Transport, Is.EqualTo(Transport));

            // Heartbeats keep it alive past its 1 s TTL.
            yield return WaitUntil(() => _rendezvous.Heartbeats >= 3, "the server never sent heartbeats");
            Assert.That(_rendezvous.Find(code), Is.Not.Null, "still registered after several TTLs");

            // The player types it lower case with spaces.
            string typed = Server.PublishedCodeText.ToLowerInvariant().Replace("-", " ");
            Task<bool> join = Client.JoinByCode(typed, ClientConfig());
            yield return WaitFor(join);
            Assert.That(join.Result, Is.True, "JoinByCode: " + string.Join(" | ", _clientFailures));
            yield return WaitUntil(() => Client.State == SessionState.Connected && Server.ConnectedClientIds.Count == 1, () => "the client never connected by code (client " + Client.State + ")");
            Assert.That(Client.Config.Transport, Is.EqualTo(Transport), "the transport came from the registration");
            Assert.That(Client.Config.Port, Is.EqualTo(Port));

            Client.Leave();
            Server.Leave();
            yield return WaitUntil(() => Server.State == SessionState.Idle && _rendezvous.Count == 0, () => "the code was not withdrawn when the server left (" + _rendezvous.Count + " registrations)");
            Assert.That(Server.PublishedCode.IsEmpty, Is.True);

            // The old code now leads nowhere.
            yield return WaitUntil(() => Client.CanStart, "the client never finished leaving");
            Task<bool> stale = Client.JoinByCode(code.Value, ClientConfig());
            yield return WaitFor(stale);
            Assert.That(stale.Result, Is.False);
            Assert.That(_clientFailures, Has.Some.Contains("No game is using code"));
            Assert.That(Client.State, Is.Not.EqualTo(SessionState.Connecting));
        }

        [UnityTest]
        public IEnumerator ALostRegistration_IsRegisteredAgainWithANewCode()
        {
            yield return StartDedicatedServerAndGetCode();
            JoinCode first = Server.PublishedCode;

            _rendezvous.ForgetAll(); // the service restarted
            yield return WaitUntil(() => !Server.PublishedCode.IsEmpty && Server.PublishedCode != first, "the server did not register again after its registration was lost");
            Assert.That(_rendezvous.Find(Server.PublishedCode), Is.Not.Null);
            Assert.That(Server.State, Is.EqualTo(SessionState.Running), "losing a registration does not stop the server");
        }

        [UnityTest]
        public IEnumerator RunPod_RegistersTheMappedTcpPortAndPublicIp()
        {
            var env = new Dictionary<string, string>
            {
                { "RUNPOD_TCP_PORT_" + Port, "40999" },
                { "RUNPOD_PUBLIC_IP", "203.0.113.50" },
            };
            Server.EnvironmentReader = name => env.TryGetValue(name, out string value) ? value : null;
            if (Transport == TransportKind.Udp)
            {
                // RunPod maps TCP only; the server warns and registers its own UDP port.
                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("RunPod maps port .* over TCP only"));
            }

            yield return StartDedicatedServerAndGetCode();
            InProcessRendezvousServer.Entry entry = _rendezvous.Find(Server.PublishedCode);
            Assert.That(entry, Is.Not.Null);
            if (Transport == TransportKind.WebSocket)
            {
                Assert.That(entry.Port, Is.EqualTo(40999), "the mapped port, not the pod-internal one");
                Assert.That(entry.Address, Is.EqualTo("203.0.113.50"));
            }
            else
            {
                Assert.That(entry.Port, Is.EqualTo(Port));
                Assert.That(entry.Address, Is.EqualTo(NetConfig.LoopbackAddress));
            }
        }

        [UnityTest]
        public IEnumerator WrongSecret_ReportsTheFailureAndKeepsServing()
        {
            NetConfig config = ServerConfig();
            config.RendezvousSecret = "wrong";
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Could not register this server.*registration secret"));
            Assert.That(Server.StartServer(config), Is.True);
            yield return WaitUntil(() => _serverFailures.Count > 0, "no JoinCodeFailed for a wrong secret");
            Assert.That(_serverFailures[0], Does.Contain("Wrong or missing registration secret."));
            Assert.That(Server.State, Is.EqualTo(SessionState.Running));
            Assert.That(Server.PublishedCode.IsEmpty, Is.True);
        }

        [UnityTest]
        public IEnumerator ListenServer_HostWithCode_RendezvousPublishesTheHostsAddress()
        {
            NetConfig config = ServerConfig();
            config.JoinCodes = JoinCodeProvider.Rendezvous;
            config.PublicAddress = NetConfig.LoopbackAddress;
            Task<JoinCode> host = Server.HostWithCode(config);
            yield return WaitFor(host);
            Assert.That(host.Result.IsEmpty, Is.False, string.Join(" | ", _serverFailures));
            Assert.That(Server.IsHost, Is.True);

            Task<bool> join = Client.JoinByCode(JoinCodeFormat.Format(host.Result.Value), ClientConfig());
            yield return WaitFor(join);
            Assert.That(join.Result, Is.True, string.Join(" | ", _clientFailures));
            yield return WaitUntil(() => Client.State == SessionState.Connected, "the client never connected to the host by code");
        }

        [UnityTest]
        public IEnumerator ListenServer_WithNothingSetUp_DefaultsToRelay()
        {
            Assert.That(JoinCodeDefaults.ForPublishing(Topology.Host, ServerConfig().JoinCodes), Is.EqualTo(JoinCodeProvider.Relay));
            if (RelayJoinCodeService.IsAvailable)
            {
                Assert.Ignore("com.unity.services.multiplayer is installed; a real Relay allocation needs a linked Cloud project.");
            }

            NetConfig config = ServerConfig();
            config.RendezvousUrl = null; // nothing but the default: Relay is chosen, and its absence reported
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("com.unity.services.multiplayer"));
            Task<JoinCode> host = Server.HostWithCode(config);
            yield return WaitFor(host);
            Assert.That(host.Result.IsEmpty, Is.True);
            Assert.That(_serverFailures, Has.Some.Contains("com.unity.services.multiplayer"), "Relay was chosen and its absence reported");
            Assert.That(Server.IsActive, Is.False, "no host is started without its Relay allocation");
            Assert.That(_rendezvous.Count, Is.EqualTo(0), "no rendezvous URL, so the rendezvous service is not used");
        }

        [UnityTest]
        public IEnumerator ListenServer_DefaultJoinCodes_WithoutRelay_UseTheRendezvousUrl()
        {
            if (RelayJoinCodeService.IsAvailable)
            {
                Assert.Ignore("com.unity.services.multiplayer is installed; Auto picks Relay first.");
            }

            // UM-T63: JoinCodes left at Auto with a rendezvous URL configured publishes through the rendezvous service.
            NetConfig config = ServerConfig();
            Assert.That(config.JoinCodes, Is.EqualTo(JoinCodeProvider.Auto));
            config.PublicAddress = NetConfig.LoopbackAddress;
            Task<JoinCode> host = Server.HostWithCode(config);
            yield return WaitFor(host);
            Assert.That(host.Result.IsEmpty, Is.False, string.Join(" | ", _serverFailures));
            Assert.That(_rendezvous.Find(host.Result), Is.Not.Null, "registered with the rendezvous service");

            Task<bool> join = Client.JoinByCode(JoinCodeFormat.Format(host.Result.Value), ClientConfig());
            yield return WaitFor(join);
            Assert.That(join.Result, Is.True, string.Join(" | ", _clientFailures));
            yield return WaitUntil(() => Client.State == SessionState.Connected, "the client never connected to the host by code");
        }

        [UnityTest]
        public IEnumerator BadOrUnknownCodes_FailWithAReason()
        {
            Task<bool> typo = Client.JoinByCode("not a code!", ClientConfig());
            yield return WaitFor(typo);
            Assert.That(typo.Result, Is.False);

            Task<bool> unknown = Client.JoinByCode(JoinCode.Generate().Value, ClientConfig());
            yield return WaitFor(unknown);
            Assert.That(unknown.Result, Is.False);
            Assert.That(_clientFailures, Has.Count.EqualTo(2), string.Join(" | ", _clientFailures));
            Assert.That(_clientFailures[1], Does.Contain("No game is using code"));

            Task<bool> noService = Client.JoinByCode(JoinCode.Generate().Value, new NetConfig { GameVersion = "test", JoinCodes = JoinCodeProvider.Rendezvous });
            yield return WaitFor(noService);
            Assert.That(noService.Result, Is.False);
            Assert.That(_clientFailures[2], Does.Contain("RendezvousUrl"));
            Assert.That(Client.State, Is.EqualTo(SessionState.Idle));
        }

        [UnityTest]
        public IEnumerator UnreachableRendezvous_FailsTheJoinWithAReason()
        {
            var config = new NetConfig { GameVersion = "test", RendezvousUrl = "http://127.0.0.1:1" };
            Task<bool> join = Client.JoinByCode(JoinCode.Generate().Value, config);
            yield return NetAwait.ForTask(join, "the join", 15f);
            Assert.That(join.Result, Is.False);
            Assert.That(_clientFailures, Has.Some.Contains("Could not look up code"));
        }
    }
}
