using System;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Unity.Multiplayer.Core.Tests
{
    [TestFixture]
    public class GameyeMatchAllocatorTests
    {
        private static readonly GameyePortSpec[] Ports = { new GameyePortSpec("game", 7777, TransportKind.Udp) };

        private FakeGameyeApiClient api;
        private InMemoryJoinCodeService joinCodes;
        private GameyeMatchAllocator allocator;

        [SetUp]
        public void SetUp()
        {
            api = new FakeGameyeApiClient();
            joinCodes = new InMemoryJoinCodeService();
            allocator = new GameyeMatchAllocator(api, joinCodes, "my-game", "eu-west", Ports)
            {
                PollInterval = TimeSpan.FromMilliseconds(1),
                Timeout = TimeSpan.FromSeconds(5),
            };
        }

        [Test]
        public void Constructor_RequiresNonEmptyArguments()
        {
            Assert.Throws<ArgumentNullException>(() => new GameyeMatchAllocator(null, joinCodes, "img", "loc", Ports));
            Assert.Throws<ArgumentNullException>(() => new GameyeMatchAllocator(api, null, "img", "loc", Ports));
            Assert.Throws<ArgumentException>(() => new GameyeMatchAllocator(api, joinCodes, "", "loc", Ports));
            Assert.Throws<ArgumentException>(() => new GameyeMatchAllocator(api, joinCodes, "img", "", Ports));
            Assert.Throws<ArgumentNullException>(() => new GameyeMatchAllocator(api, joinCodes, "img", "loc", null));
            Assert.Throws<ArgumentException>(() => new GameyeMatchAllocator(api, joinCodes, "img", "loc", Array.Empty<GameyePortSpec>()));
            Assert.Throws<ArgumentException>(() => new GameyeMatchAllocator(api, joinCodes, "img", "loc", Ports, null, ""));
        }

        [Test]
        public void DisplayName_IsGameye()
        {
            Assert.That(allocator.DisplayName, Is.EqualTo("Gameye"));
        }

        [Test]
        public async Task RegisterImageAsync_SendsImageAndRegistryCredentials()
        {
            await allocator.RegisterImageAsync("ghcr.io/org/game:v1", "me", "token");

            Assert.That(api.LastRegistration.ImageName, Is.EqualTo("my-game"));
            Assert.That(api.LastRegistration.ImageReference, Is.EqualTo("ghcr.io/org/game:v1"));
            Assert.That(api.LastRegistration.RegistryUsername, Is.EqualTo("me"));
            Assert.That(api.LastRegistration.RegistryPassword, Is.EqualTo("token"));
        }

        [Test]
        public void RegisterImageAsync_NoImageReference_Throws()
        {
            Assert.ThrowsAsync<ArgumentException>(() => allocator.RegisterImageAsync(""));
        }

        [Test]
        public async Task AllocateAsync_PollsUntilReady_PublishesCode_ReturnsEndpoint()
        {
            api.ReadyAfterPolls = 2;
            MatchAllocation allocation = await allocator.AllocateAsync(new MatchRequest("match-1", 4));

            Assert.That(allocation.AllocationId, Is.EqualTo("session-1"));
            Assert.That(allocation.Endpoint, Is.EqualTo(JoinTarget.ForEndpoint(api.Ip, (ushort)api.Port, TransportKind.Udp)));
            Assert.That(allocation.Code.IsEmpty, Is.False);

            JoinTarget resolved = await joinCodes.ResolveAsync(allocation.Code);
            Assert.That(resolved, Is.EqualTo(allocation.Endpoint));
            Assert.That(api.LastStartRequest.ImageName, Is.EqualTo("my-game"));
            Assert.That(api.LastStartRequest.Location, Is.EqualTo("eu-west"));
            Assert.That(api.LastStartRequest.MatchId, Is.EqualTo("match-1"));
            Assert.That(api.Calls, Does.Contain("StartSession"));
            Assert.That(api.Calls, Does.Contain("GetSession"));
        }

        [Test]
        public void AllocateAsync_NullRequest_Throws()
        {
            Assert.ThrowsAsync<ArgumentNullException>(() => allocator.AllocateAsync(null));
        }

        [Test]
        public void AllocateAsync_ProviderRefusal_PropagatesAllocationException()
        {
            api.FailStartWithAllocationException = true;
            Assert.ThrowsAsync<MatchAllocationException>(() => allocator.AllocateAsync(new MatchRequest("match-1", 2)));
        }

        [Test]
        public void AllocateAsync_ProviderThrowsGenericException_WrapsInAllocationException()
        {
            api.FailStartWithGenericException = true;
            var ex = Assert.ThrowsAsync<MatchAllocationException>(() => allocator.AllocateAsync(new MatchRequest("match-1", 2)));
            Assert.That(ex.Message, Does.Contain("starting a session"));
        }

        [Test]
        public void AllocateAsync_NeverBecomesReady_TimesOutAndStops()
        {
            api.ReadyAfterPolls = int.MaxValue;
            allocator.Timeout = TimeSpan.FromMilliseconds(20);
            var ex = Assert.ThrowsAsync<MatchAllocationException>(() => allocator.AllocateAsync(new MatchRequest("match-1", 2)));
            Assert.That(ex.Message, Does.Contain("did not start"));
            Assert.That(api.StoppedSessionId, Is.EqualTo("session-1"));
        }

        [Test]
        public void AllocateAsync_PortNotMapped_ThrowsAndStops()
        {
            api.ReportedPortName = "voice";
            var ex = Assert.ThrowsAsync<MatchAllocationException>(() => allocator.AllocateAsync(new MatchRequest("match-1", 2)));
            Assert.That(ex.Message, Does.Contain("no 'game' port mapped"));
            Assert.That(api.StoppedSessionId, Is.EqualTo("session-1"));
        }

        [Test]
        public async Task ReleaseAsync_RevokesCodeAndStopsSession()
        {
            MatchAllocation allocation = await allocator.AllocateAsync(new MatchRequest("match-1", 2));
            await allocator.ReleaseAsync(allocation);

            Assert.That(await joinCodes.ResolveAsync(allocation.Code), Is.Null);
            Assert.That(api.StoppedSessionId, Is.EqualTo(allocation.AllocationId));
        }

        [Test]
        public void ReleaseAsync_NullAllocation_Throws()
        {
            Assert.ThrowsAsync<ArgumentNullException>(() => allocator.ReleaseAsync(null));
        }
    }
}
