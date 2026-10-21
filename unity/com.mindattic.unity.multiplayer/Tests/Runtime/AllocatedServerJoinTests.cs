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
    /// UM-T29: joining a server a <see cref="IMatchAllocator"/> stood up for a match, against a
    /// <see cref="FakeMatchAllocator"/> that hands back a real NetcodeIntegrationTest server's loopback
    /// endpoint. No real Edgegap/Gameye account is touched here -- that is what the opt-in
    /// <c>EdgegapSmokeTests</c>/<c>GameyeSmokeTests</c> in Core.Tests are for.
    /// </summary>
    [TestFixture(TransportKind.Udp, (ushort)47921)]
    [TestFixture(TransportKind.WebSocket, (ushort)47922)]
    public class AllocatedServerJoinTests : NetcodeIntegrationTest
    {
        private readonly TransportKind _transport;
        private readonly ushort _port;
        private NetSession _server;
        private NetSession _client;
        private FakeMatchAllocator _allocator;
        private readonly List<string> _clientFailures = new List<string>();

        public AllocatedServerJoinTests(TransportKind transport, ushort port) : base(HostOrServer.Server)
        {
            _transport = transport;
            _port = port;
        }

        protected override int NumberOfClients => 1;

        protected override bool CanStartServerAndClients() => false;

        protected override void OnServerAndClientsCreated()
        {
            NetSessionTestSupport.PrepareManagers(m_NetworkManagers);
            _server = new NetSession(m_ServerNetworkManager);
            _client = new NetSession(m_ClientNetworkManagers[0]);
            _allocator = new FakeMatchAllocator(JoinTarget.ForEndpoint("127.0.0.1", _port, _transport));
            _clientFailures.Clear();
            _client.JoinCodeFailed += _clientFailures.Add;
            base.OnServerAndClientsCreated();
        }

        protected override IEnumerator OnTearDown()
        {
            _client?.Dispose();
            _server?.Dispose();
            _client = null;
            _server = null;
            yield return null;
        }

        private NetConfig ServerConfig()
        {
            return new NetConfig { Port = _port, Transport = _transport, GameVersion = "test" };
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

        [UnityTest]
        public IEnumerator Client_JoinsAllocatedServer_ThroughFakeAllocator()
        {
            Assert.That(_server.StartServer(ServerConfig()), Is.True, "StartServer");
            yield return WaitForConditionOrTimeOut(() => _server.State == SessionState.Running);
            AssertOnTimeout("the dedicated server never started");

            var request = new MatchRequest("match-1", 2);
            Task<bool> join = _client.JoinAllocatedServerAsync(_allocator, request, new NetConfig { GameVersion = "test" });
            yield return Await(join);
            Assert.That(join.Result, Is.True, string.Join(" | ", _clientFailures));

            yield return WaitForConditionOrTimeOut(() => _client.State == SessionState.Connected);
            AssertOnTimeout("the client never connected (state " + _client.State + ")");

            Assert.That(_allocator.Requests, Is.EqualTo(new[] { request }));
            Assert.That(_client.JoinedAllocation, Is.Not.Null);
            Assert.That(_client.JoinedAllocation.AllocationId, Is.EqualTo("fake-1"));
            Assert.That(_client.JoinedAllocation.Endpoint, Is.EqualTo(JoinTarget.ForEndpoint("127.0.0.1", _port, _transport)));

            // Releasing is the matchmaker's job once the match ends, not NetSession's -- exercised directly.
            yield return Await(_allocator.ReleaseAsync(_client.JoinedAllocation));
            Assert.That(_allocator.Released, Has.Count.EqualTo(1));
            Assert.That(_allocator.Released[0], Is.SameAs(_client.JoinedAllocation));
        }

        [UnityTest]
        public IEnumerator Client_AllocatorRefuses_RaisesJoinCodeFailedAndStaysIdle()
        {
            _allocator.Fail = true;
            Task<bool> join = _client.JoinAllocatedServerAsync(_allocator, new MatchRequest("match-1", 2), new NetConfig { GameVersion = "test" });
            yield return Await(join);

            Assert.That(join.Result, Is.False);
            Assert.That(_clientFailures, Has.Count.EqualTo(1));
            Assert.That(_clientFailures[0], Does.Contain("fake allocator refused"));
            Assert.That(_client.JoinedAllocation, Is.Null);
            Assert.That(_client.State, Is.EqualTo(SessionState.Idle));
        }
    }
}
