using System;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Unity.Multiplayer.Core.Tests
{
    [TestFixture]
    public class EdgegapMatchAllocatorTests
    {
        private FakeEdgegapApiClient api;
        private InMemoryJoinCodeService joinCodes;
        private EdgegapMatchAllocator allocator;

        [SetUp]
        public void SetUp()
        {
            api = new FakeEdgegapApiClient();
            joinCodes = new InMemoryJoinCodeService();
            allocator = new EdgegapMatchAllocator(api, joinCodes, "my-app", "v1")
            {
                PollInterval = TimeSpan.FromMilliseconds(1),
                Timeout = TimeSpan.FromSeconds(5),
            };
        }

        [Test]
        public void Constructor_RequiresNonEmptyArguments()
        {
            Assert.Throws<ArgumentNullException>(() => new EdgegapMatchAllocator(null, joinCodes, "app", "v1"));
            Assert.Throws<ArgumentNullException>(() => new EdgegapMatchAllocator(api, null, "app", "v1"));
            Assert.Throws<ArgumentException>(() => new EdgegapMatchAllocator(api, joinCodes, "", "v1"));
            Assert.Throws<ArgumentException>(() => new EdgegapMatchAllocator(api, joinCodes, "app", ""));
            Assert.Throws<ArgumentException>(() => new EdgegapMatchAllocator(api, joinCodes, "app", "v1", ""));
        }

        [Test]
        public void DisplayName_IsEdgegap()
        {
            Assert.That(allocator.DisplayName, Is.EqualTo("Edgegap"));
        }

        [Test]
        public async Task EnsureAppVersionAsync_SendsAppVersionImageAndPorts()
        {
            var ports = new[] { new EdgegapPortSpec("game", 7777, TransportKind.Udp) };
            await allocator.EnsureAppVersionAsync("ghcr.io/org/game:v1", ports);

            Assert.That(api.LastAppVersionRequest.AppName, Is.EqualTo("my-app"));
            Assert.That(api.LastAppVersionRequest.VersionName, Is.EqualTo("v1"));
            Assert.That(api.LastAppVersionRequest.ImageReference, Is.EqualTo("ghcr.io/org/game:v1"));
            Assert.That(api.LastAppVersionRequest.Ports, Is.EqualTo(ports));
        }

        [Test]
        public void EnsureAppVersionAsync_NoPorts_Throws()
        {
            Assert.ThrowsAsync<ArgumentException>(() => allocator.EnsureAppVersionAsync("ghcr.io/org/game:v1", Array.Empty<EdgegapPortSpec>()));
        }

        [Test]
        public async Task AllocateAsync_PollsUntilRunning_PublishesCode_ReturnsEndpoint()
        {
            api.RunningAfterPolls = 2;
            MatchAllocation allocation = await allocator.AllocateAsync(new MatchRequest("match-1", 4));

            Assert.That(allocation.AllocationId, Is.EqualTo("req-1"));
            Assert.That(allocation.Endpoint, Is.EqualTo(JoinTarget.ForEndpoint(api.PublicIp, (ushort)api.ExternalPort, TransportKind.Udp)));
            Assert.That(allocation.Code.IsEmpty, Is.False);

            JoinTarget resolved = await joinCodes.ResolveAsync(allocation.Code);
            Assert.That(resolved, Is.EqualTo(allocation.Endpoint));
            Assert.That(api.Calls, Does.Contain("Deploy"));
            Assert.That(api.Calls, Does.Contain("GetDeployment"));
        }

        [Test]
        public void AllocateAsync_NullRequest_Throws()
        {
            Assert.ThrowsAsync<ArgumentNullException>(() => allocator.AllocateAsync(null));
        }

        [Test]
        public void AllocateAsync_ProviderRefusal_PropagatesAllocationException()
        {
            api.FailDeployWithAllocationException = true;
            Assert.ThrowsAsync<MatchAllocationException>(() => allocator.AllocateAsync(new MatchRequest("match-1", 2)));
        }

        [Test]
        public void AllocateAsync_ProviderThrowsGenericException_WrapsInAllocationException()
        {
            api.FailDeployWithGenericException = true;
            var ex = Assert.ThrowsAsync<MatchAllocationException>(() => allocator.AllocateAsync(new MatchRequest("match-1", 2)));
            Assert.That(ex.Message, Does.Contain("requesting a deployment"));
        }

        [Test]
        public void AllocateAsync_NeverBecomesRunning_TimesOutAndStops()
        {
            api.RunningAfterPolls = int.MaxValue;
            allocator.Timeout = TimeSpan.FromMilliseconds(20);
            var ex = Assert.ThrowsAsync<MatchAllocationException>(() => allocator.AllocateAsync(new MatchRequest("match-1", 2)));
            Assert.That(ex.Message, Does.Contain("did not start"));
            Assert.That(api.StoppedRequestId, Is.EqualTo("req-1"));
        }

        [Test]
        public void AllocateAsync_PortNotMapped_ThrowsAndStops()
        {
            api.ReportedPortName = "voice";
            var ex = Assert.ThrowsAsync<MatchAllocationException>(() => allocator.AllocateAsync(new MatchRequest("match-1", 2)));
            Assert.That(ex.Message, Does.Contain("no 'game' port mapped"));
            Assert.That(api.StoppedRequestId, Is.EqualTo("req-1"));
        }

        [Test]
        public async Task ReleaseAsync_RevokesCodeAndStopsDeployment()
        {
            MatchAllocation allocation = await allocator.AllocateAsync(new MatchRequest("match-1", 2));
            await allocator.ReleaseAsync(allocation);

            Assert.That(await joinCodes.ResolveAsync(allocation.Code), Is.Null);
            Assert.That(api.StoppedRequestId, Is.EqualTo(allocation.AllocationId));
        }

        [Test]
        public void ReleaseAsync_NullAllocation_Throws()
        {
            Assert.ThrowsAsync<ArgumentNullException>(() => allocator.ReleaseAsync(null));
        }
    }
}
