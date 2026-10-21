using System.Collections;
using System.Collections.Generic;
using Unity.Multiplayer.Core;
using Unity.Multiplayer.Testing;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Unity.Multiplayer.Tests
{
    /// <summary>
    /// UM-T29: joining a server a <see cref="IMatchAllocator"/> stood up for a match, against a
    /// <see cref="FakeMatchAllocator"/> that hands back a real dedicated server's loopback endpoint (the test kit's
    /// <see cref="NetSessionTest"/>). No real Edgegap/Gameye account is touched here -- that is what the opt-in
    /// <c>EdgegapSmokeTests</c>/<c>GameyeSmokeTests</c> in Core.Tests are for.
    /// </summary>
    [TestFixture(TransportKind.Udp)]
    [TestFixture(TransportKind.WebSocket)]
    public class AllocatedServerJoinTests : NetSessionTest
    {
        private FakeMatchAllocator _allocator;
        private readonly List<string> _clientFailures = new List<string>();

        public AllocatedServerJoinTests(TransportKind transport) : base(Topology.Server, clients: 1, transport: transport)
        {
        }

        protected override void OnSessionsCreated()
        {
            _allocator = new FakeMatchAllocator(JoinTarget.ForEndpoint("127.0.0.1", Port, Transport));
            _clientFailures.Clear();
            Client.JoinCodeFailed += _clientFailures.Add;
        }

        [UnityTest]
        public IEnumerator Client_JoinsAllocatedServer_ThroughFakeAllocator()
        {
            yield return StartServer();

            var request = new MatchRequest("match-1", 2);
            Awaiting<bool> join = WaitFor(Client.JoinAllocatedServerAsync(_allocator, request, new NetConfig { GameVersion = GameVersion }), "the allocated join");
            yield return join;
            Assert.That(join.Result, Is.True, string.Join(" | ", _clientFailures));

            yield return WaitUntil(() => Client.State == SessionState.Connected, () => "the client never connected (state " + Client.State + ")");

            Assert.That(_allocator.Requests, Is.EqualTo(new[] { request }));
            Assert.That(Client.JoinedAllocation, Is.Not.Null);
            Assert.That(Client.JoinedAllocation.AllocationId, Is.EqualTo("fake-1"));
            Assert.That(Client.JoinedAllocation.Endpoint, Is.EqualTo(JoinTarget.ForEndpoint("127.0.0.1", Port, Transport)));

            // Releasing is the matchmaker's job once the match ends, not NetSession's -- exercised directly.
            yield return WaitFor(_allocator.ReleaseAsync(Client.JoinedAllocation), "the release");
            Assert.That(_allocator.Released, Has.Count.EqualTo(1));
            Assert.That(_allocator.Released[0], Is.SameAs(Client.JoinedAllocation));
        }

        [UnityTest]
        public IEnumerator Client_AllocatorRefuses_RaisesJoinCodeFailedAndStaysIdle()
        {
            _allocator.Fail = true;
            Awaiting<bool> join = WaitFor(Client.JoinAllocatedServerAsync(_allocator, new MatchRequest("match-1", 2), new NetConfig { GameVersion = GameVersion }));
            yield return join;

            Assert.That(join.Result, Is.False);
            Assert.That(_clientFailures, Has.Count.EqualTo(1));
            Assert.That(_clientFailures[0], Does.Contain("fake allocator refused"));
            Assert.That(Client.JoinedAllocation, Is.Null);
            Assert.That(Client.State, Is.EqualTo(SessionState.Idle));
        }
    }
}
