using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Multiplayer.Core;

namespace Unity.Multiplayer.Tests
{
    /// <summary>
    /// An <see cref="IMatchAllocator"/> that always "allocates" the same <see cref="JoinTarget"/>
    /// (a real NetcodeIntegrationTest server's loopback endpoint), publishing a real join code through
    /// an in-memory <see cref="IJoinCodeService"/> so <see cref="AllocatedServerJoinTests"/> exercises
    /// <see cref="NetSession.JoinAllocatedServerAsync"/> end to end without any real provider.
    /// </summary>
    internal sealed class FakeMatchAllocator : IMatchAllocator
    {
        private readonly JoinTarget target;
        private readonly IJoinCodeService joinCodes;
        private int counter;

        public FakeMatchAllocator(JoinTarget target, IJoinCodeService joinCodes = null)
        {
            this.target = target;
            this.joinCodes = joinCodes ?? new InMemoryJoinCodeService();
        }

        /// <summary>Every request <see cref="AllocateAsync"/> was called with, in order.</summary>
        public List<MatchRequest> Requests { get; } = new List<MatchRequest>();

        /// <summary>Every allocation <see cref="ReleaseAsync"/> was called with, in order.</summary>
        public List<MatchAllocation> Released { get; } = new List<MatchAllocation>();

        /// <summary>When true, <see cref="AllocateAsync"/> throws instead of allocating.</summary>
        public bool Fail { get; set; }

        public string DisplayName
        {
            get { return "Fake"; }
        }

        public async Task<MatchAllocation> AllocateAsync(MatchRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (Fail)
            {
                throw new MatchAllocationException("fake allocator refused.");
            }

            JoinCode code = await joinCodes.PublishAsync(target, cancellationToken);
            counter++;
            return new MatchAllocation("fake-" + counter, target, code);
        }

        public Task ReleaseAsync(MatchAllocation allocation, CancellationToken cancellationToken = default)
        {
            Released.Add(allocation);
            return Task.CompletedTask;
        }
    }
}
