using System.Threading;
using System.Threading.Tasks;

namespace Unity.Multiplayer.Core
{
    /// <summary>
    /// Requests and releases one server per match from a game-server platform that allocates on
    /// demand (Edgegap, Gameye, ...), as distinct from <c>IHostingProfile</c>'s provision-once,
    /// keep-running profiles (GenericVps, RunPod and the rest of RFC 0001's hosting table). A
    /// matchmaker calls <see cref="AllocateAsync"/> when a match is ready to start and
    /// <see cref="ReleaseAsync"/> once it ends; the adapter's <c>NetSession.JoinAllocatedServerAsync</c>
    /// is the client side, joining the endpoint an allocation carries.
    /// </summary>
    /// <remarks>
    /// Contract every implementation keeps:
    /// <list type="bullet">
    /// <item><see cref="AllocateAsync"/> does not return until the server is actually reachable at
    /// <see cref="MatchAllocation.Endpoint"/> and <see cref="MatchAllocation.Code"/> already resolves
    /// to it.</item>
    /// <item><see cref="ReleaseAsync"/> is idempotent: releasing an allocation twice, or one the
    /// provider no longer knows about, does nothing and does not throw.</item>
    /// <item>Both throw <see cref="MatchAllocationException"/> (never a provider-specific exception
    /// type) when the provider refuses the request or cannot be reached.</item>
    /// </list>
    /// </remarks>
    public interface IMatchAllocator
    {
        /// <summary>A name for people and for logs ("Edgegap", "Gameye", ...).</summary>
        string DisplayName { get; }

        /// <summary>Requests a server for <paramref name="request"/> and waits until it can be joined.</summary>
        /// <exception cref="System.ArgumentNullException"><paramref name="request"/> is null.</exception>
        /// <exception cref="MatchAllocationException">The provider refused the request, could not be reached, or never came up in time.</exception>
        Task<MatchAllocation> AllocateAsync(MatchRequest request, CancellationToken cancellationToken = default);

        /// <summary>Tears down a server <see cref="AllocateAsync"/> returned, once the match is over.</summary>
        /// <exception cref="System.ArgumentNullException"><paramref name="allocation"/> is null.</exception>
        /// <exception cref="MatchAllocationException">The provider could not be reached (a refusal because the allocation is already gone is swallowed, not thrown).</exception>
        Task ReleaseAsync(MatchAllocation allocation, CancellationToken cancellationToken = default);
    }
}
