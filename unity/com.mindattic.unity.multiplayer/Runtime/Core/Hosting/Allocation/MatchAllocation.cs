using System;

namespace Unity.Multiplayer.Core
{
    /// <summary>
    /// A server an <see cref="IMatchAllocator"/> stood up for one match: where a client joins it
    /// (<see cref="Endpoint"/>, also reachable through <see cref="Code"/>) and the provider's own id
    /// for it, which <see cref="IMatchAllocator.ReleaseAsync"/> needs back once the match ends.
    /// </summary>
    public sealed class MatchAllocation
    {
        /// <summary>Creates an allocation result.</summary>
        /// <exception cref="ArgumentException"><paramref name="allocationId"/> is empty, or <paramref name="code"/> is empty.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="endpoint"/> is null.</exception>
        public MatchAllocation(string allocationId, JoinTarget endpoint, JoinCode code)
        {
            if (string.IsNullOrWhiteSpace(allocationId))
            {
                throw new ArgumentException("An allocation needs the provider's id for it.", nameof(allocationId));
            }

            if (endpoint == null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }

            if (code.IsEmpty)
            {
                throw new ArgumentException("An allocation needs a join code.", nameof(code));
            }

            AllocationId = allocationId.Trim();
            Endpoint = endpoint;
            Code = code;
        }

        /// <summary>The provider's own id for this deployment/session (an Edgegap <c>request_id</c>, a Gameye session id, ...).</summary>
        public string AllocationId { get; }

        /// <summary>Where the allocated server is reachable; joined directly with the adapter's <c>NetSession</c>, or by <see cref="Code"/>.</summary>
        public JoinTarget Endpoint { get; }

        /// <summary>The join code published for <see cref="Endpoint"/>, to hand a player instead of a raw address.</summary>
        public JoinCode Code { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            return AllocationId + " at " + Endpoint + " (code " + Code + ")";
        }
    }
}
