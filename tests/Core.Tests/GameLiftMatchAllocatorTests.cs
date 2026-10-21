using System;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Unity.Multiplayer.Core.Tests
{
    [TestFixture]
    public class GameLiftMatchAllocatorTests
    {
        private FakeGameLiftApiClient api;
        private InMemoryJoinCodeService joinCodes;
        private GameLiftMatchAllocator allocator;

        [SetUp]
        public void SetUp()
        {
            api = new FakeGameLiftApiClient();
            joinCodes = new InMemoryJoinCodeService();
            allocator = new GameLiftMatchAllocator(api, joinCodes, "fleet-1")
            {
                PollInterval = TimeSpan.FromMilliseconds(1),
                Timeout = TimeSpan.FromSeconds(5),
            };
        }

        [Test]
        public void Constructor_RequiresNonEmptyArguments()
        {
            Assert.Throws<ArgumentNullException>(() => new GameLiftMatchAllocator(null, joinCodes, "fleet-1"));
            Assert.Throws<ArgumentNullException>(() => new GameLiftMatchAllocator(api, null, "fleet-1"));
            Assert.Throws<ArgumentException>(() => new GameLiftMatchAllocator(api, joinCodes, ""));
        }

        [Test]
        public void DisplayName_IsAwsGameLift()
        {
            Assert.That(allocator.DisplayName, Is.EqualTo("AWS GameLift"));
        }

        [Test]
        public async Task AllocateAsync_PollsUntilActive_PublishesCode_ReturnsEndpoint()
        {
            api.ActiveAfterPolls = 2;
            MatchAllocation allocation = await allocator.AllocateAsync(new MatchRequest("match-1", 4));

            Assert.That(allocation.AllocationId, Is.EqualTo("gsess-1"));
            Assert.That(allocation.Endpoint, Is.EqualTo(JoinTarget.ForEndpoint(api.IpAddress, (ushort)api.Port, TransportKind.Udp)));
            Assert.That(allocation.Code.IsEmpty, Is.False);

            JoinTarget resolved = await joinCodes.ResolveAsync(allocation.Code);
            Assert.That(resolved, Is.EqualTo(allocation.Endpoint));
            Assert.That(api.Calls, Does.Contain("CreateGameSession"));
            Assert.That(api.Calls, Does.Contain("DescribeGameSession"));
            Assert.That(api.LastCreateRequest.FleetId, Is.EqualTo("fleet-1"));
            Assert.That(api.LastCreateRequest.MaximumPlayerSessionCount, Is.EqualTo(4));
            Assert.That(api.LastCreateRequest.Name, Is.EqualTo("match-1"));
        }

        [Test]
        public void AllocateAsync_UsesAliasInsteadOfFleet_WhenGiven()
        {
            var aliased = new GameLiftMatchAllocator(api, joinCodes, "fleet-1", "alias-1") { PollInterval = TimeSpan.FromMilliseconds(1) };
            Assert.That(aliased.AliasId, Is.EqualTo("alias-1"));
        }

        [Test]
        public void AllocateAsync_NullRequest_Throws()
        {
            Assert.ThrowsAsync<ArgumentNullException>(() => allocator.AllocateAsync(null));
        }

        [Test]
        public void AllocateAsync_ProviderRefusal_PropagatesAllocationException()
        {
            api.FailCreateWithAllocationException = true;
            Assert.ThrowsAsync<MatchAllocationException>(() => allocator.AllocateAsync(new MatchRequest("match-1", 2)));
        }

        [Test]
        public void AllocateAsync_ProviderThrowsGenericException_WrapsInAllocationException()
        {
            api.FailCreateWithGenericException = true;
            var ex = Assert.ThrowsAsync<MatchAllocationException>(() => allocator.AllocateAsync(new MatchRequest("match-1", 2)));
            Assert.That(ex.Message, Does.Contain("requesting a game session"));
        }

        [Test]
        public void AllocateAsync_NeverBecomesActive_TimesOut()
        {
            api.ActiveAfterPolls = int.MaxValue;
            allocator.Timeout = TimeSpan.FromMilliseconds(20);
            var ex = Assert.ThrowsAsync<MatchAllocationException>(() => allocator.AllocateAsync(new MatchRequest("match-1", 2)));
            Assert.That(ex.Message, Does.Contain("did not activate"));
        }

        [Test]
        public void AllocateAsync_SessionGoesTerminal_Throws()
        {
            api.TerminalStatus = "ERROR";
            var ex = Assert.ThrowsAsync<MatchAllocationException>(() => allocator.AllocateAsync(new MatchRequest("match-1", 2)));
            Assert.That(ex.Message, Does.Contain("ended up ERROR"));
        }

        [Test]
        public async Task ReleaseAsync_RevokesCode_DoesNotThrow()
        {
            MatchAllocation allocation = await allocator.AllocateAsync(new MatchRequest("match-1", 2));
            await allocator.ReleaseAsync(allocation);

            Assert.That(await joinCodes.ResolveAsync(allocation.Code), Is.Null);
        }

        [Test]
        public void ReleaseAsync_NullAllocation_Throws()
        {
            Assert.ThrowsAsync<ArgumentNullException>(() => allocator.ReleaseAsync(null));
        }

        [Test]
        public async Task CreatePlayerSessionAsync_ReturnsPlayerSessionId()
        {
            MatchAllocation allocation = await allocator.AllocateAsync(new MatchRequest("match-1", 2));
            string playerSessionId = await allocator.CreatePlayerSessionAsync(allocation, "player-1", "nickname=Ryan");

            Assert.That(playerSessionId, Is.EqualTo(api.CreatedPlayerSessionId));
            Assert.That(api.LastPlayerId, Is.EqualTo("player-1"));
            Assert.That(api.LastPlayerData, Is.EqualTo("nickname=Ryan"));
        }

        [Test]
        public void CreatePlayerSessionAsync_NullAllocation_Throws()
        {
            Assert.ThrowsAsync<ArgumentNullException>(() => allocator.CreatePlayerSessionAsync(null, "player-1"));
        }

        [Test]
        public async Task CreatePlayerSessionAsync_EmptyPlayerId_Throws()
        {
            MatchAllocation allocation = await allocator.AllocateAsync(new MatchRequest("match-1", 2));
            Assert.ThrowsAsync<ArgumentException>(() => allocator.CreatePlayerSessionAsync(allocation, ""));
        }

        [Test]
        public async Task CreatePlayerSessionAsync_ProviderThrowsGenericException_WrapsInAllocationException()
        {
            MatchAllocation allocation = await allocator.AllocateAsync(new MatchRequest("match-1", 2));
            api.FailCreatePlayerSessionWithGenericException = true;
            var ex = Assert.ThrowsAsync<MatchAllocationException>(() => allocator.CreatePlayerSessionAsync(allocation, "player-1"));
            Assert.That(ex.Message, Does.Contain("creating a player session"));
        }
    }
}
